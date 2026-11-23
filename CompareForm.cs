using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PhotoDuplicateFinder
{
    internal sealed class CompareForm : Form
    {
        public CompareForm(Matches group, string selected)
        {
            Text = "Compare versions";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 580);
            MinimumSize = new Size(620, 380);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(columns);
            int current = group.Paths.IndexOf(selected);
            columns.Controls.Add(VersionPanel(group, current, "Left version"), 0, 0);
            columns.Controls.Add(VersionPanel(group, current == 0 ? 1 : 0, "Right version"), 1, 0);
        }

        private Control VersionPanel(Matches group, int initial, string name)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = name };
            foreach (string path in group.Paths) choice.Items.Add(Path.GetFileName(path) + "   ·   " + group.Versions[path].Resolution);
            var view = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(241, 244, 248) };
            var info = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            panel.Controls.Add(choice, 0, 0);
            panel.Controls.Add(view, 0, 1);
            panel.Controls.Add(info, 0, 2);
            int request = 0;
            panel.Disposed += delegate { request++; if (view.Image != null) view.Image.Dispose(); };
            choice.SelectedIndexChanged += async delegate
            {
                int version = ++request;
                string path = group.Paths[choice.SelectedIndex];
                var photo = group.Versions[path];
                info.Text = photo.Resolution + " px   ·   " + MainForm.SizeText(photo.Size) + Environment.NewLine + path;
                var old = view.Image;
                view.Image = null;
                if (old != null) old.Dispose();
                try
                {
                    var image = await Task.Run(() => MainForm.LoadPreview(path));
                    if (version == request && !view.IsDisposed) view.Image = image;
                    else image.Dispose();
                }
                catch (Exception) { if (version == request && !info.IsDisposed) info.AppendText(Environment.NewLine + "Preview unavailable."); }
            };
            choice.SelectedIndex = initial;
            return panel;
        }
    }
}
