namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    internal static class BoundedInputReader
    {
        public static async Task<byte[]> ReadAllBytesAsync(
            Stream source,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!source.CanRead)
                throw new ArgumentException("输入流必须可读。", nameof(source));
            if (maximumBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (source.CanSeek && source.Length - source.Position > maximumBytes)
                throw new InvalidDataException($"输入超过 {maximumBytes} 字节上限。");

            using var destination = new MemoryStream();
            byte[] buffer = new byte[81920];
            int totalBytes = 0;

            while (true)
            {
                int bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                totalBytes = checked(totalBytes + bytesRead);
                if (totalBytes > maximumBytes)
                    throw new InvalidDataException($"输入超过 {maximumBytes} 字节上限。");

                await destination
                    .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);
            }

            return destination.ToArray();
        }
    }
}
