using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormRuntimeServices
    {
        private SettingsFormRuntimeServices(
            ICs2UpdateReminderService cs2UpdateReminder,
            IYouPinInventoryService youPinInventory)
        {
            Cs2UpdateReminder = cs2UpdateReminder ?? throw new ArgumentNullException(nameof(cs2UpdateReminder));
            YouPinInventory = youPinInventory ?? throw new ArgumentNullException(nameof(youPinInventory));
        }

        public ICs2UpdateReminderService Cs2UpdateReminder { get; }

        public IYouPinInventoryService YouPinInventory { get; }

        public static SettingsFormRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static SettingsFormRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new SettingsFormRuntimeServices(
                provider.GetRequiredService<ICs2UpdateReminderService>(),
                provider.GetRequiredService<IYouPinInventoryService>());
        }
    }
}
