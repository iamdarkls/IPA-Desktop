namespace IpaDesktop
{
    /// <summary>Сохраняемые настройки и независимые указатели текущих и прежних версий инструментов.</summary>
    public class State
    {
        public State()
        {
            AutoUpdate = true;
            SoundsEnabled = true;
            AutoUpdateDeviceTools = true;
            CheckGuiUpdates = true;
        }
        public bool SoundsEnabled { get; set; }
        public State Copy() { return (State)MemberwiseClone(); }
        public bool AutoUpdateDeviceTools { get; set; }
        public string PreviousDeviceCurrent { get; set; }
        public string PreviousDeviceVersion { get; set; }
        public string Source { get; set; }
        public string PreviousSource { get; set; }
        public string DeviceSource { get; set; }
        public string PreviousDeviceSource { get; set; }
        public string DeviceCurrent { get; set; }
        public string DeviceVersion { get; set; }
        public string Current
        {
            get;
            set;
        }
        public string Previous
        {
            get;
            set;
        }
        public string Version
        {
            get;
            set;
        }
        public string PreviousVersion
        {
            get;
            set;
        }
        public bool AutoUpdate
        {
            get;
            set;
        }
        public bool CheckGuiUpdates
        {
            get;
            set;
        }
    }
}
