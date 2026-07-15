namespace CS2TradeMonitor.src.SystemServices.InfoService
{
    public interface IInfoService
    {
        string GetValue(string key);

        void InjectValue(string key, string value);

        void InjectIP(string ip);

        void RemoveDataByPrefix(string prefix);

        void Update();
    }
}
