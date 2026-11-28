using System.Windows;

namespace Shortpad.Helpers
{
    public static class ControlNameHelper
    {
        private const string ButtonPrefix = "Btn";
        private const string PagePrefix = "Page";
        private const string EditPagePrefix = "EditPage";

        public static string GetButtonName(int buttonId) => $"{ButtonPrefix}{buttonId}";

        public static string GetPageName(int pageIndex) => $"{PagePrefix}{pageIndex}";

        public static string GetEditPageName(int pageIndex) => $"{EditPagePrefix}{pageIndex}";

        public static int ParseButtonId(string? controlName)
        {
            if (string.IsNullOrEmpty(controlName) || !controlName.StartsWith(ButtonPrefix))
                throw new FormatException($"无效的按钮控件名: '{controlName}'");

            if (!int.TryParse(controlName.AsSpan(ButtonPrefix.Length), out int buttonId) || buttonId < 0)
                throw new FormatException($"无法从按钮控件名解析 ID: '{controlName}'");

            return buttonId;
        }

        public static bool TryParseButtonId(string? controlName, out int buttonId)
        {
            buttonId = -1;
            if (string.IsNullOrEmpty(controlName) || !controlName.StartsWith(ButtonPrefix))
                return false;

            return int.TryParse(controlName.AsSpan(ButtonPrefix.Length), out buttonId) && buttonId >= 0;
        }

        public static int ParsePageIndex(string? controlName)
        {
            if (string.IsNullOrEmpty(controlName))
                throw new FormatException($"无效的页控件名: '{controlName}'");

            string? numberPart = null;
            if (controlName.StartsWith(EditPagePrefix))
                numberPart = controlName[EditPagePrefix.Length..];
            else if (controlName.StartsWith(PagePrefix))
                numberPart = controlName[PagePrefix.Length..];

            if (numberPart == null || !int.TryParse(numberPart, out int pageIndex) || pageIndex < 0)
                throw new FormatException($"无法从页控件名解析页索引: '{controlName}'");

            return pageIndex;
        }

        public static bool TryParsePageIndex(string? controlName, out int pageIndex)
        {
            pageIndex = -1;
            if (string.IsNullOrEmpty(controlName))
                return false;

            string? numberPart = null;
            if (controlName.StartsWith(EditPagePrefix))
                numberPart = controlName[EditPagePrefix.Length..];
            else if (controlName.StartsWith(PagePrefix))
                numberPart = controlName[PagePrefix.Length..];

            return numberPart != null && int.TryParse(numberPart, out pageIndex) && pageIndex >= 0;
        }

        public static int ParseButtonId(FrameworkElement? element) =>
            ParseButtonId(element?.Name);

        public static int ParsePageIndex(FrameworkElement? element) =>
            ParsePageIndex(element?.Name);
    }
}