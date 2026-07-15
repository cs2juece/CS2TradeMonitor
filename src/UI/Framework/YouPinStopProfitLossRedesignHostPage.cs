namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class YouPinStopProfitLossRedesignHostPage : FrameworkSettingsHostPage<YouPinStopProfitLossRedesignPage>
    {
        public YouPinStopProfitLossRedesignHostPage()
            : base(new YouPinStopProfitLossRedesignPage(YouPinPageRuntimeServices.Resolve()))
        {
        }
    }
}
