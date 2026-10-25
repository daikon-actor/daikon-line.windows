using System.Drawing;
using System.IO;

namespace DaikonLine
{
    /// <summary>exe に埋め込んだ app.ico（build.bat の /resource と csproj の LogicalName を合わせること）</summary>
    internal static class AppIcon
    {
        private const string ResourceName = "DaikonLine.app.ico";

        /// <summary>アイコンを読み込む。呼び出し側で Dispose すること。</summary>
        public static Icon Load()
        {
            using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream != null) return new Icon(stream);
            }
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
