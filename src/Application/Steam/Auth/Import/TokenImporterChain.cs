using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Application.Steam.Auth.Import
{
    public sealed class TokenImporterChain
    {
        private readonly IReadOnlyList<ITokenImporter> _importers;

        public static TokenImporterChain Default { get; } = new(new ITokenImporter[]
        {
            new OtpAuthImporter()
        });

        public TokenImporterChain(IReadOnlyList<ITokenImporter> importers)
        {
            _importers = importers ?? throw new ArgumentNullException(nameof(importers));
        }

        public TokenImportResult Import(string text, string sourcePath = "")
        {
            foreach (var importer in _importers)
            {
                if (!importer.CanImport(text, sourcePath))
                    continue;
                return importer.Import(text, sourcePath);
            }
            return TokenImportResult.Failed("未识别的令牌格式。");
        }

        public bool CanImport(string text, string sourcePath = "")
        {
            foreach (var importer in _importers)
            {
                if (importer.CanImport(text, sourcePath))
                    return true;
            }
            return false;
        }
    }
}
