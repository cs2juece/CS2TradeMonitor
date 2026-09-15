namespace CS2TradeMonitor.src.UI
{
    internal sealed class MainFormWindowPresentationState
    {
        private int _settingsWindowPresentationCount;

        public bool SettingsWindowPresentationActive => _settingsWindowPresentationCount > 0;

        public bool ResolveTopMost(bool configuredTopMost)
        {
            return configuredTopMost && !SettingsWindowPresentationActive;
        }

        public bool BeginSettingsWindowPresentation()
        {
            _settingsWindowPresentationCount++;
            return _settingsWindowPresentationCount == 1;
        }

        public bool EndSettingsWindowPresentation()
        {
            if (_settingsWindowPresentationCount <= 0)
                return false;

            _settingsWindowPresentationCount--;
            return _settingsWindowPresentationCount == 0;
        }
    }
}
