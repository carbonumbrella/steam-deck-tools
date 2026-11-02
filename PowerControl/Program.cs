using CommonHelpers;
using PowerControl.Helpers.AMD;
namespace PowerControl
{
    internal static class Program
    {
        const int MAX_GPU_RETRIES = 30;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            if (Settings.Default.EnableExperimentalFeatures)
            {
                if (!AntiCheatSettings.Default.AckAntiCheat(
                    Controller.TitleWithVersion,
                    "You are running EXPERIMENTAL build.", null, false))
                    return;
            }

            for (int i = 0; !VangoghGPU.IsSupported && i < MAX_GPU_RETRIES; i++)
            {
                var status = Instance.WithGlobalMutex(1000, () => VangoghGPU.Detect());
                Log.Info("GPU detect: attempt {0}/{1}: {2}, supported={3}, uptime={4}s",
                    i + 1, MAX_GPU_RETRIES, status, VangoghGPU.IsSupported, Environment.TickCount64 / 1000);
                if (status != VangoghGPU.DetectionStatus.Retryable)
                    break;

                Thread.Sleep(300);
            }

            if (!VangoghGPU.IsSupported)
                Log.Error("GPU detect: not supported after detection loop, TDP control unavailable.");

            using (var controller = new Controller())
            {
                Application.Run();
            }
        }
    }
}