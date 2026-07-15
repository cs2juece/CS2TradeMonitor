namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISecureDataProtector
    {
        byte[] Protect(byte[] plaintext, byte[] entropy);
        byte[] Unprotect(byte[] protectedData, byte[] entropy);
    }
}
