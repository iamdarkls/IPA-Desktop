namespace IpaDesktop
{
    /// <summary>Результат проверки драйверов Apple и сведения для объяснения пользователю.</summary>
    public enum AppleSupportStatus
    {
        Running,
        Stopped,
        ServiceMissing,
        NotDetected,
        Unknown
    }

    /// <summary>Результат проверки драйверов Apple и сведения для объяснения пользователю.</summary>
    public sealed class AppleSupportSnapshot
    {
        public bool InstallationDetected { get; set; }
        public bool ServiceFound { get; set; }
        public bool ServiceStatusKnown { get; set; }
        public bool ServiceRunning { get; set; }
        public bool ServiceScanCompleted { get; set; }
        public bool RegistryScanCompleted { get; set; }
        public string Details { get; set; }
    }

    /// <summary>Результат проверки драйверов Apple и сведения для объяснения пользователю.</summary>
    public sealed class AppleSupportReport
    {
        public AppleSupportStatus Status { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string HelpUrl
        {
            get
            {
                return Status == AppleSupportStatus.Stopped
                    ? "https://support.apple.com/ru-ru/102347"
                    : "https://support.apple.com/ru-ru/118290";
            }
        }
    }
}
