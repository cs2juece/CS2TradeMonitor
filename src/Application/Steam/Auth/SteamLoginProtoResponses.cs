using System;
using System.Collections.Generic;
using System.Globalization;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal sealed class BeginAuthResponse
    {
        public ulong ClientId { get; set; }
        public byte[] RequestId { get; set; } = Array.Empty<byte>();
        public float IntervalSeconds { get; set; } = 1;
        public List<AllowedConfirmation> AllowedConfirmations { get; } = new();
        public string SteamId { get; set; } = "";
        public string AgreementSessionUrl { get; set; } = "";
        public string ExtendedErrorMessage { get; set; } = "";
    }

    internal sealed class AllowedConfirmation
    {
        public int Type { get; set; }
        public string Message { get; set; } = "";
    }

    internal sealed class PollAuthResponse
    {
        public string RefreshToken { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public bool HadRemoteInteraction { get; set; }
        public string AccountName { get; set; } = "";
        public string NewGuardData { get; set; } = "";
        public string AgreementSessionUrl { get; set; } = "";
    }

    internal static class SteamLoginProtoResponseParser
    {
        public static BeginAuthResponse DecodeBeginAuthResponse(byte[] response)
        {
            var reader = new SteamProtoReader(response);
            var result = new BeginAuthResponse();
            while (reader.TryReadField(out int field, out int wire))
            {
                switch (field)
                {
                    case 1:
                        result.ClientId = reader.ReadUInt64(wire);
                        break;
                    case 2:
                        result.RequestId = reader.ReadBytes(wire);
                        break;
                    case 3:
                        result.IntervalSeconds = reader.ReadFloat(wire);
                        break;
                    case 4:
                        result.AllowedConfirmations.Add(DecodeAllowedConfirmation(reader.ReadBytes(wire)));
                        break;
                    case 5:
                        result.SteamId = reader.ReadUInt64(wire).ToString(CultureInfo.InvariantCulture);
                        break;
                    case 7:
                        result.AgreementSessionUrl = reader.ReadString(wire);
                        break;
                    case 8:
                        result.ExtendedErrorMessage = reader.ReadString(wire);
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }
            return result;
        }

        public static AllowedConfirmation DecodeAllowedConfirmation(byte[] bytes)
        {
            var reader = new SteamProtoReader(bytes);
            var result = new AllowedConfirmation();
            while (reader.TryReadField(out int field, out int wire))
            {
                switch (field)
                {
                    case 1:
                        result.Type = (int)reader.ReadUInt64(wire);
                        break;
                    case 2:
                        result.Message = reader.ReadString(wire);
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }
            return result;
        }

        public static PollAuthResponse DecodePollAuthResponse(byte[] response)
        {
            var reader = new SteamProtoReader(response);
            var result = new PollAuthResponse();
            while (reader.TryReadField(out int field, out int wire))
            {
                switch (field)
                {
                    case 3:
                        result.RefreshToken = reader.ReadString(wire);
                        break;
                    case 4:
                        result.AccessToken = reader.ReadString(wire);
                        break;
                    case 5:
                        result.HadRemoteInteraction = reader.ReadBool(wire);
                        break;
                    case 6:
                        result.AccountName = reader.ReadString(wire);
                        break;
                    case 7:
                        result.NewGuardData = reader.ReadString(wire);
                        break;
                    case 8:
                        result.AgreementSessionUrl = reader.ReadString(wire);
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }
            return result;
        }
    }
}
