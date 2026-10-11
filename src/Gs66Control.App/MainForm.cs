using System.Text;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.App;

internal sealed class MainForm : Form
{
    private readonly Button _diagnose = new() { Text = "实机只读诊断", AutoSize = true };
    private readonly Button _simulate = new() { Text = "模拟演示", AutoSize = true };
    private readonly Button _export = new() { Text = "导出 JSON", AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "取消", AutoSize = true, Enabled = false };
    private readonly Label _source = new() { Text = "尚未采集 · 启动不会访问硬件", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Text = "请选择实机诊断或模拟演示。", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Dictionary<string, Label> _values = new();
    private readonly TextBox _classes = ReadOnlyText();
    private readonly TextBox _warnings = ReadOnlyText();
    private readonly TextBox _json = ReadOnlyText();
    private HardwareSnapshot? _snapshot;
    private CancellationTokenSource? _cancellation;
    private bool _closing;

    public MainForm()
    {
        Text = "GS66 只读诊断";
        ClientSize = new Size(900, 680);
        MinimumSize = new Size(720, 550);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "GS66 硬件与固件信息", AutoSize = true,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "仅查询 WMI。当前不测量温度、风扇转速或 EC 版本，也不提供控制操作。",
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), WrapContents = true
        };
        buttons.Controls.AddRange([_diagnose, _simulate, _export, _cancel]);
        layout.Controls.Add(buttons, 0, 2);
        _source.Font = new Font(Font, FontStyle.Bold);
        _source.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(_source, 0, 3);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var overview = new TabPage("设备概览") { Padding = new Padding(12), AutoScroll = true };
        var details = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var name in new[] { "厂商", "型号", "CPU", "GPU", "BIOS 版本", "操作系统", "采集时间", "温度 / 风扇 / EC" })
        {
            var row = details.RowCount++;
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.Controls.Add(new Label { Text = name, AutoSize = true, Margin = new Padding(0, 8, 8, 8) }, 0, row);
            var value = new Label { Text = "尚未采集", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
            details.Controls.Add(value, 1, row);
            _values.Add(name, value);
        }
        overview.Controls.Add(details);
        tabs.TabPages.Add(overview);
        AddTextTab(tabs, "MSI WMI 类", _classes);
        AddTextTab(tabs, "缺失读数与警告", _warnings);
        AddTextTab(tabs, "导出预览", _json);
        layout.Controls.Add(tabs, 0, 4);
        _status.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(_status, 0, 5);
        Controls.Add(layout);

        _diagnose.Click += async (_, _) => await CaptureAsync(new WindowsWmiProbe(), false);
        _simulate.Click += async (_, _) => await CaptureAsync(new SimulatedHardwareProbe(), true);
        _export.Click += async (_, _) => await ExportAsync();
        _cancel.Click += (_, _) =>
        {
            _cancellation?.Cancel();
            _cancel.Enabled = false;
            _status.Text = "已请求取消；正在进行的 WMI 查询可能需要时间返回。";
        };
    }

    private async Task CaptureAsync(IHardwareProbe probe, bool simulated)
    {
        if (_cancellation is not null || _closing) return;
        using var cancellation = BeginOperation();
        _snapshot = null;
        foreach (var value in _values.Values) value.Text = "等待采集";
        _classes.Clear();
        _warnings.Clear();
        _json.Clear();
        _source.Text = simulated ? "正在生成模拟数据 · 不是实机读数" : "正在进行实机只读 WMI 诊断";
        _source.ForeColor = simulated ? Color.DarkOrange : Color.DarkSlateBlue;
        _status.Text = "正在采集；可取消，也可安全关闭窗口。";
        try
        {
            var snapshot = await probe.CaptureAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closing) return;
            _snapshot = snapshot;
            ShowSnapshot(snapshot);
            _status.Text = "采集完成。导出预览展示将保存的 JSON，可先检查再分享。";
        }
        catch (OperationCanceledException)
        {
            if (_closing) return;
            _source.Text = "采集已取消 · 没有新读数";
            _status.Text = "可以重新选择实机诊断或模拟演示。";
            foreach (var value in _values.Values) value.Text = "未采集";
        }
        catch (Exception ex)
        {
            if (_closing) return;
            _source.Text = "采集失败 · 没有新读数";
            _status.Text = $"诊断未完成（{ex.GetType().Name}）。请查看警告后重试。";
            _warnings.Text = $"查询发生错误：{ex.GetType().Name}。\r\n没有生成硬件快照。";
            foreach (var value in _values.Values) value.Text = "未采集";
        }
        finally { EndOperation(cancellation); }
    }

    private void ShowSnapshot(HardwareSnapshot snapshot)
    {
        _source.Text = snapshot.IsSimulated
            ? $"模拟演示 · 所有数据均为示例（来源：{snapshot.Source}）"
            : $"实机只读诊断 · WMI 查询结果（来源：{snapshot.Source}）";
        _source.ForeColor = snapshot.IsSimulated ? Color.DarkOrange : Color.DarkGreen;
        _values["厂商"].Text = Present(snapshot.Manufacturer);
        _values["型号"].Text = Present(snapshot.Model);
        _values["CPU"].Text = Present(snapshot.Cpu);
        _values["GPU"].Text = snapshot.Gpus.Count > 0 ? string.Join(Environment.NewLine, snapshot.Gpus) : "未取得读数";
        _values["BIOS 版本"].Text = Present(snapshot.BiosVersion);
        _values["操作系统"].Text = snapshot.OperatingSystem;
        _values["采集时间"].Text = snapshot.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        _values["温度 / 风扇 / EC"].Text = "未测量；当前探针不支持这些读数";
        _classes.Text = DescribeClasses(snapshot);
        var warnings = new List<string> { "温度、风扇转速、风扇曲线与 EC 固件版本：未测量。" };
        if (snapshot.IsSimulated) warnings.Add("当前为模拟演示；没有查询真实硬件或固件。");
        foreach (var (name, value) in new[] { ("厂商", snapshot.Manufacturer), ("型号", snapshot.Model), ("CPU", snapshot.Cpu), ("BIOS 版本", snapshot.BiosVersion) })
            if (string.IsNullOrWhiteSpace(value)) warnings.Add($"{name}：未取得读数。");
        if (snapshot.Gpus.Count == 0) warnings.Add("GPU：未取得读数。");
        warnings.AddRange(snapshot.Warnings);
        _warnings.Text = string.Join(Environment.NewLine + Environment.NewLine, warnings);
        _json.Text = SnapshotExport.Serialize(snapshot);
    }

    private async Task ExportAsync()
    {
        if (_snapshot is null || _cancellation is not null || _closing) return;
        using var dialog = new SaveFileDialog
        {
            Title = "保存诊断 JSON", Filter = "JSON 文件 (*.json)|*.json", DefaultExt = "json", AddExtension = true,
            FileName = $"gs66-diagnostic-{(_snapshot.IsSimulated ? "simulation" : "wmi")}-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var json = SnapshotExport.Serialize(_snapshot);
        using var cancellation = BeginOperation();
        var temporaryPath = dialog.FileName + $".{Guid.NewGuid():N}.tmp";
        _status.Text = "正在保存 JSON…";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, dialog.FileName, overwrite: true);
            if (!_closing) _status.Text = "JSON 已保存。导出包含设备型号与软件版本，不含序列号或用户信息。";
        }
        catch (OperationCanceledException)
        {
            if (!_closing) _status.Text = "保存已取消；可重新导出。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_closing) _status.Text = $"保存失败（{ex.GetType().Name}）。请选择可写入的文件夹后重试。";
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            EndOperation(cancellation);
        }
    }

    private CancellationTokenSource BeginOperation()
    {
        _cancellation = new CancellationTokenSource();
        _diagnose.Enabled = _simulate.Enabled = _export.Enabled = false;
        _cancel.Enabled = true;
        UseWaitCursor = true;
        return _cancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
        if (_closing) return;
        _diagnose.Enabled = _simulate.Enabled = true;
        _export.Enabled = _snapshot is not null;
        _cancel.Enabled = false;
        UseWaitCursor = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        _closing = true;
        _cancellation?.Cancel();
    }

    private static string Present(string? value) => string.IsNullOrWhiteSpace(value) ? "未取得读数" : value;

    private static string DescribeClasses(HardwareSnapshot snapshot)
    {
        if (snapshot.IsSimulated)
            return "模拟演示未查询 WMI 类。请运行实机只读诊断获取可见类信息。";
        if (snapshot.MsiWmiClasses.Count == 0)
            return "没有发现 MSI 命名的 WMI 类。不能据此判断硬件是否支持控制。";

        var text = new StringBuilder("仅查询类定义；不读取 MSI 实例，也不执行方法。\r\n类名、GUID 或方法可见不表示已经验证控制支持。\r\n\r\n");
        text.AppendLine("可见类名：");
        foreach (var name in snapshot.MsiWmiClasses) text.AppendLine(name);
        text.AppendLine();
        if (snapshot.MsiWmiSchemas.Count == 0)
            text.AppendLine("未取得类定义详情，请查看查询警告。");
        foreach (var schema in snapshot.MsiWmiSchemas)
        {
            text.AppendLine($"类：{schema.Name}");
            text.AppendLine($"  GUID：{Present(schema.Guid)}");
            text.AppendLine("  属性：");
            if (schema.Properties.Count == 0) text.AppendLine("    未取得属性定义");
            foreach (var property in schema.Properties)
                text.AppendLine($"    {property.Name}: {property.Type}{(property.IsArray ? "[]" : "")}{(property.IsKey ? " (Key)" : "")}");
            text.AppendLine("  方法定义（不会执行）：");
            if (schema.Methods.Count == 0) text.AppendLine("    无可见方法定义");
            foreach (var method in schema.Methods) text.AppendLine($"    {method}");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static TextBox ReadOnlyText() => new()
    {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both,
        WordWrap = false, BackColor = SystemColors.Window, BorderStyle = BorderStyle.None
    };
    private static void AddTextTab(TabControl tabs, string title, TextBox text)
    {
        var tab = new TabPage(title) { Padding = new Padding(12) };
        tab.Controls.Add(text);
        tabs.TabPages.Add(tab);
    }
}
