namespace HowToFish.RouletteTrainer.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = GameInstallation.SelfTest();
            return;
        }
        if (args.Length >= 2 && args[0].Equals("--install", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                new GameInstallation(args[1]).Install(args.Length >= 3
                    ? Path.GetFullPath(args[2])
                    : Path.Combine(AppContext.BaseDirectory, "payload"));
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 2;
            }
            return;
        }
        if (args.Length == 2 && args[0].Equals("--restore", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                new GameInstallation(args[1]).Restore();
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 2;
            }
            return;
        }
        if (args.Length == 3 && args[0].Equals("--set-mode", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                new GameInstallation(args[1]).SetMode(args[2]);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 2;
            }
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
