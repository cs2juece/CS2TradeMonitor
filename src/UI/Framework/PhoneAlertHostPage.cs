namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class PhoneAlertHostPage : FrameworkSettingsHostPage<PhoneAlertPage>
    {
        public PhoneAlertHostPage()
            : base(new PhoneAlertPage(YouPinPageRuntimeServices.Resolve()))
        {
        }
    }
}
