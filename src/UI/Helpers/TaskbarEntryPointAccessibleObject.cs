using System;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Helpers
{
    internal sealed class TaskbarEntryPointAccessibleObject : Control.ControlAccessibleObject
    {
        private readonly Action _defaultAction;

        public TaskbarEntryPointAccessibleObject(Control owner, Action defaultAction)
            : base(owner)
        {
            ArgumentNullException.ThrowIfNull(defaultAction);
            _defaultAction = defaultAction;
        }

        public override string? Name
        {
            get => "CS2交易监控任务栏入口";
            set { }
        }

        public override string? Description => "双击或执行默认操作以运行已配置的任务栏动作";

        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override string? DefaultAction => "执行已配置的双击动作";

        public override void DoDefaultAction() => _defaultAction();
    }
}
