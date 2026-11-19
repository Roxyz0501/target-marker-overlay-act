using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Roxyz.Act.UI;

internal static class PreviewTest
{
    static T Field<T>(object host, string name)
    {
        return (T)host.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(host);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name + " " + ex.Message + Environment.NewLine + ex.StackTrace);
            return 1;
        }
    }

    static void Run(string[] args)
    {
        var asm = Assembly.LoadFrom(args[0]);
        int count = 0;
        foreach (string lang in new[] { "ja", "en", "ko", "zh-CN" })
        {
            var settings = Activator.CreateInstance(asm.GetType("FoodBuffOverlay.PluginSettings"));
            settings.GetType().GetProperty("Language").SetValue(settings, lang, null);
            using (var reference = (Control)Activator.CreateInstance(asm.GetType("FoodBuffOverlay.SettingsControl"), settings))
            using (var page = new PluginUpdatePage("food-buff-overlay", "1.6.0", true, lang))
            using (var form = new UserControl { Size = new Size(720, 440) })
            using (var tabs = new TabControl { Dock = DockStyle.Fill })
            {
                var original = Field<TabPage>(reference, "updateTab");
                tabs.TabPages.Add(original); tabs.TabPages.Add(page); form.Controls.Add(tabs); form.CreateControl(); tabs.CreateControl();
                foreach (int width in new[] { 360, 720 })
                foreach (UpdatePageState state in Enum.GetValues(typeof(UpdatePageState)))
                {
                    string message = state == UpdatePageState.Available ? (lang == "ja" ? "更新があります: 1.6.0 → 1.7.0\r\n更新内容のサンプル" : "1.6.0 → 1.7.0") : state.ToString();
                    form.ClientSize = new Size(width,440);
                    bool offer = state == UpdatePageState.Available || state == UpdatePageState.PrepareFailed;
                    Field<Button>(reference,"updateNow").Visible = Field<Button>(reference,"later").Visible = offer || state == UpdatePageState.Preparing;
                    Field<Button>(reference,"updateNow").Enabled = Field<Button>(reference,"later").Enabled = offer;
                    Field<Button>(reference,"checkNow").Enabled = state != UpdatePageState.Checking && state != UpdatePageState.Preparing && state != UpdatePageState.Prepared;
                    Field<Label>(reference,"updateStatus").Text = message;
                    page.ShowState(state,message);
                    tabs.SelectedTab = original; Application.DoEvents();
                    using (var expected = new Bitmap(original.Width,original.Height))
                    {
                        original.DrawToBitmap(expected,original.ClientRectangle);
                        tabs.SelectedTab = page; Application.DoEvents();
                        using(var actual=new Bitmap(page.Width,page.Height))
                        {
                            page.DrawToBitmap(actual,page.ClientRectangle);
                            if (actual.Width < 300 || actual.Height < 300) throw new Exception("Reference page was not laid out.");
                            int nonWhite = 0;
                            for (int y=0;y<actual.Height;y++)
                            for (int x=0;x<actual.Width;x++)
                            {
                                if (actual.GetPixel(x,y).ToArgb() != Color.White.ToArgb()) nonWhite++;
                                if(actual.GetPixel(x,y)!=expected.GetPixel(x,y))
                                    throw new Exception("Rendering mismatch: " + lang + " " + state + " " + width + " at " + x + "," + y);
                            }
                            if (nonWhite < 200) throw new Exception("Reference image has no visible UI content.");
                            if(width==720 && lang=="ja" && state==UpdatePageState.Available) actual.Save("UpdatePage-ja.png");
                        }
                    }
                    count++;
                }
                // Exercise repeated assignment and returning to a previously used language.
                foreach (var code in new[] { lang, "en", "ja", "zh-CN", "ko", lang, lang })
                {
                    page.SetLanguage(code);
                    var button = Field<Button>(page, "checkNow");
                    using (var graphics = button.CreateGraphics())
                        graphics.MeasureString(button.Text, button.Font);
                }
            }
        }
        Console.WriteLine(count + " pixel-identical reference comparisons passed.");
    }
}
