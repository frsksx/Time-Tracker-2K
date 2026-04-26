namespace TimeTracker2K;

internal static class Program
{
    private const string MutexName = "Local\\TimeTracker2K";
    private const string LegacyMutexName = "Local\\LoginDurationTracker";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        using var legacyMutex = new Mutex(true, LegacyMutexName, out var legacyCreatedNew);
        if (!createdNew || !legacyCreatedNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
