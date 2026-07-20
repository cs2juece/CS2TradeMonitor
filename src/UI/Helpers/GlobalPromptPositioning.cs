using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Helpers
{
    internal static class GlobalPromptPositioning
    {
        public static Form? ResolveStableOwner(IWin32Window? requestedOwner, Size dialogSize)
        {
            Form? requestedForm = requestedOwner switch
            {
                Form form => form,
                Control control when !control.IsDisposed => control.FindForm(),
                _ => null
            };

            if (IsSuitableOwner(requestedForm, dialogSize))
                return requestedForm;

            return System.Windows.Forms.Application.OpenForms
                .Cast<Form>()
                .Where(form => IsSuitableOwner(form, dialogSize))
                .OrderByDescending(form => (long)form.Width * form.Height)
                .FirstOrDefault();
        }

        public static Screen ResolveScreen(IWin32Window? requestedOwner, Form? stableOwner)
        {
            if (stableOwner != null)
                return Screen.FromControl(stableOwner);

            if (requestedOwner is Control control && !control.IsDisposed && control.IsHandleCreated)
                return Screen.FromControl(control);

            if (requestedOwner is not Control && requestedOwner != null)
            {
                try
                {
                    if (requestedOwner.Handle != IntPtr.Zero)
                        return Screen.FromHandle(requestedOwner.Handle);
                }
                catch (InvalidOperationException)
                {
                    // Fall back to the pointer's screen when the native owner is no longer valid.
                }
            }

            return Screen.FromPoint(Cursor.Position);
        }

        public static Point CalculateLocation(Rectangle workingArea, Size dialogSize, Rectangle? ownerBounds)
        {
            int dialogWidth = Math.Max(1, dialogSize.Width);
            int dialogHeight = Math.Max(1, dialogSize.Height);
            Rectangle target = ownerBounds.HasValue && IsSuitableOwnerBounds(ownerBounds.Value, dialogSize)
                ? ownerBounds.Value
                : workingArea;

            int centeredX = target.Left + ((target.Width - dialogWidth) / 2);
            int centeredY = target.Top + ((target.Height - dialogHeight) / 2);
            int maxX = Math.Max(workingArea.Left, workingArea.Right - dialogWidth);
            int maxY = Math.Max(workingArea.Top, workingArea.Bottom - dialogHeight);

            return new Point(
                Math.Clamp(centeredX, workingArea.Left, maxX),
                Math.Clamp(centeredY, workingArea.Top, maxY));
        }

        private static bool IsSuitableOwner(Form? form, Size dialogSize)
        {
            return form != null
                && !form.IsDisposed
                && form.Visible
                && form.WindowState != FormWindowState.Minimized
                && form is not GlobalPromptDialog
                && form is not MainForm
                && form is not TaskbarForm
                && IsSuitableOwnerBounds(form.Bounds, dialogSize);
        }

        private static bool IsSuitableOwnerBounds(Rectangle bounds, Size dialogSize)
        {
            int minimumWidth = Math.Max(1, dialogSize.Width / 2);
            int minimumHeight = Math.Max(1, dialogSize.Height / 2);
            return bounds.Width >= minimumWidth && bounds.Height >= minimumHeight;
        }
    }
}
