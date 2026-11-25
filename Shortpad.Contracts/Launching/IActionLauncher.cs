namespace Shortpad.Contracts.Launching
{
    public interface IActionLauncher
    {
        Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken ct = default);
    }
}