#!/usr/bin/env python3
"""Summarize bounded GS66 lab captures; RPM is a noise proxy, not sound pressure."""
import argparse
from datetime import datetime
import json
import math
import re
from pathlib import Path
import statistics
import zipfile


def percentile(values, fraction=.95):
    if not values:
        return None
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    lower = math.floor(position)
    return ordered[lower] + (ordered[min(lower + 1, len(ordered) - 1)] - ordered[lower]) * (position - lower)


def timestamp(value):
    value = re.sub(r'\.(\d+)(?=[+-]|$)', lambda match: '.' + match[1][:6].ljust(6, '0'), value.replace('Z', '+00:00'))
    return datetime.fromisoformat(value)


def summarize(folder):
    result = json.loads((folder / 'result.json').read_text(encoding='utf-8-sig'))
    path = folder / 'samples.jsonl'
    rows = [json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines()] if path.exists() else []
    if result.get('IsSimulated') is not False or any(row['Telemetry'].get('IsSimulated') is not False for row in rows):
        raise ValueError('Capture provenance is simulated or unspecified: ' + folder.name)
    report = {'job': folder.name, 'passed': result['Passed'], 'restored': result['Restored'],
              'samples': len(rows), 'target': result.get('Target'), 'curve': result.get('Curve'), 'error': result.get('Error')}
    durations = result.get('TaskMilliseconds') or []
    report.update(taskBatches=len(durations), taskMedianMs=statistics.median(durations) if durations else None,
                  taskP95Ms=percentile(durations))
    telemetry = [row['Telemetry'] for row in rows]
    valid, standby = 0, 0
    for sample in telemetry:
        power = sample.get('GpuPower') or {}
        age = (timestamp(sample['CapturedAtUtc']) - timestamp(power['CapturedAtUtc'])).total_seconds() if power.get('CapturedAtUtc') else math.inf
        sleep = (sample.get('GpuTemperatureCelsius') is None and power.get('IsSimulated') is False
                 and power.get('DevicePowerState') == 4 and power.get('Source') == 'Windows CM_POWER_DATA; NVIDIA GeForce RTX 2060'
                 and 0 <= age <= 3)
        standby += sleep
        valid += (sample.get('IsAcOnline') is not None and sample.get('IsBatteryCharging') is not None
                  and sample.get('CpuTemperatureCelsius') is not None
                  and (sample.get('GpuTemperatureCelsius') is not None or sleep)
                  and sample.get('CpuFanRpm') is not None and sample.get('GpuFanRpm') is not None
                  and sample.get('CpuLoadPercent') is not None)
    report.update(validSamples=valid, gpuStandbyFraction=standby / len(rows) if rows else None,
                  durationSeconds=(timestamp(telemetry[-1]['CapturedAtUtc']) - timestamp(telemetry[0]['CapturedAtUtc'])).total_seconds() if rows else 0,
                  powerStates=sorted({str((s.get('IsAcOnline'), s.get('IsBatteryCharging'))) for s in telemetry}))
    for key in ['CpuLoadPercent', 'CpuTemperatureCelsius', 'GpuTemperatureCelsius', 'CpuFanRpm', 'GpuFanRpm']:
        values = [sample[key] for sample in telemetry if sample.get(key) is not None]
        report[key] = {'n': len(values), 'mean': statistics.mean(values) if values else None,
                       'p95': percentile(values), 'max': max(values) if values else None}
    report['fanAbove3000SampleFraction'] = (sum(max(s.get('CpuFanRpm') or 0, s.get('GpuFanRpm') or 0) > 3000 for s in telemetry) / len(rows)) if rows else None
    report['rapidRpmIncreases'] = sum(any(a.get(k) is not None and b.get(k) is not None and b[k] - a[k] >= 400 for k in ['CpuFanRpm', 'GpuFanRpm']) for a, b in zip(telemetry, telemetry[1:]))
    report['warnings'] = sorted({w for sample in telemetry for w in sample.get('Warnings', [])})
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    folder = args.capture
    if folder.suffix == '.zip':
        folder = args.output.parent / (args.capture.stem + '-normalized')
        folder.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(args.capture) as archive:
            for entry in archive.infolist():
                name = Path(entry.filename.replace('\\', '/'))
                if name.is_absolute() or '..' in name.parts:
                    raise ValueError('Unsafe archive entry')
                target = folder / name
                if entry.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(archive.read(entry))
    reports = [summarize(path.parent) for path in sorted(folder.rglob('result.json')) if (path.parent / 'samples.jsonl').exists()]
    if not reports:
        raise ValueError('No recorded experiments were found')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({'noiseUnit': 'RPM proxy; no dBA measurement', 'experiments': reports}, indent=2))
    for report in reports:
        print(report['job'], 'PASS' if report['passed'] and report['restored'] else 'UNCONFIRMED',
              'task median', round(report['taskMedianMs'], 2) if report['taskMedianMs'] else None,
              'CPU mean/max', round(report['CpuTemperatureCelsius']['mean'], 1), report['CpuTemperatureCelsius']['max'],
              'CPU RPM mean/P95', round(report['CpuFanRpm']['mean']), round(report['CpuFanRpm']['p95']),
              'GPU D3', round(100 * report['gpuStandbyFraction'], 1), 'valid', report['validSamples'])


if __name__ == '__main__':
    main()
