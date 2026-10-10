namespace Gs66Control.Core;

public interface IHardwareProbe
{
    Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
