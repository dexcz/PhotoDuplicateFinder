using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace PhotoDuplicateFinder
{
    internal sealed class MainForm : Form
    {
        private readonly Button browse = Button("Choose folder…");
        private readonly Button scan = Button("Scan");
        private readonly Button cancel = Button("Cancel scan");
        private readonly Button mark = Button("Check extra copies");
        private readonly Button compare = Button("Compare versions…");
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, AccessibleName = "Match mode" };
        private readonly Button unmark = Button("Clear checks");
        private readonly Button recycle = Button("Move checked to Recycle Bin…");
        private readonly CheckBox recursive = new CheckBox { Text = "Include subfolders", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly TextBox folder = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly ListView files = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true,
            HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, ShowItemToolTips = true };
        private readonly PictureBox picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(241, 244, 248) };
        private readonly Label detail = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly Label status = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Label checkedInfo = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private CancellationTokenSource cancellation;
        private bool working, bulk;
        private int previewVersion;

        public MainForm()
        {
            Text = "Photo Duplicate Finder";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1040, 600);
            MinimumSize = new Size(835, 485);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;

            var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), ColumnCount = 1, RowCount = 7 };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++)
                page.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
            Controls.Add(page);
            page.Controls.Add(new Label { Text = "Photo Duplicate Finder", AutoSize = true, Font = new Font(Font.FontFamily, 19, FontStyle.Bold) }, 0, 0);
            var location = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, RowCount = 1, ColumnCount = 2 };
            location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            location.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folder.AccessibleName = "Selected folder";
            folder.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            location.Controls.Add(folder, 0, 0);
            location.Controls.Add(browse, 1, 0);
            page.Controls.Add(location, 0, 1);
            mode.Items.AddRange(new object[] { "Exact files", "Similar photos" });
            mode.SelectedIndex = 0;
            page.Controls.Add(Row(new Label { Text = "Find", AutoSize = true, Anchor = AnchorStyles.Left }, mode, recursive, scan, cancel), 0, 2);
            page.Controls.Add(status, 0, 3);

            var review = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            review.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            review.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            review.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            files.AccessibleName = "Matching photos";
            files.Columns.Add("File", 205);
            files.Columns.Add("Size", 85);
            files.Columns.Add("Resolution", 0);
            files.Columns.Add("Folder", 300);
            review.Controls.Add(files, 0, 0);
            var preview = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill };
            preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
            preview.Controls.Add(picture, 0, 0);
            preview.Controls.Add(detail, 0, 1);
            review.Controls.Add(preview, 1, 0);
            page.Controls.Add(review, 0, 4);
            page.Controls.Add(checkedInfo, 0, 5);
            page.Controls.Add(Row(compare, mark, unmark, recycle), 0, 6);

            browse.Click += delegate
            {
                using (var picker = new FolderBrowserDialog { Description = "Choose a folder to scan for duplicate photos",
                    SelectedPath = folder.Text, ShowNewFolderButton = false })
                    if (picker.ShowDialog(this) == DialogResult.OK) { folder.Text = picker.SelectedPath; ResetResults(); }
            };
            recursive.CheckedChanged += delegate { ResetResults(); };
            mode.SelectedIndexChanged += delegate { ResetResults(); };
            compare.Click += delegate { CompareSelected(); };
            files.DoubleClick += delegate { CompareSelected(); };
            scan.Click += async delegate { await ScanAsync(); };
            cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            mark.Click += delegate { CheckExtras(true); };
            unmark.Click += delegate { CheckExtras(false); };
            recycle.Click += async delegate { await RecycleAsync(); };
            files.SelectedIndexChanged += async delegate { compare.Enabled = !working && files.SelectedItems.Count == 1; await ShowPreviewAsync(); };
            files.ItemCheck += OnCheck;
            files.ItemChecked += delegate { if (!bulk) RefreshActions(); };
            FormClosing += (s, e) =>
            {
                if (working && e.CloseReason == CloseReason.UserClosing)
                {
                    if (cancellation != null) cancellation.Cancel();
                    e.Cancel = true;
                }
            };
            FormClosed += delegate { previewVersion++; SetPicture(null); };
            ResetResults();
        }

        private static Button Button(string text)
        {
            return new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            row.Controls.AddRange(controls);
            return row;
        }

        internal static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KiB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString("0.#") + " MiB";
            return (bytes / 1073741824.0).ToString("0.##") + " GiB";
        }

        private void ResetResults()
        {
            bulk = true;
            files.Items.Clear();
            files.Groups.Clear();
            bulk = false;
            previewVersion++;
            SetPicture(null);
            detail.Text = "Select a file to preview it.";
            status.Text = folder.Text.Length == 0 ? "Choose a folder to scan." : "Ready to scan.";
            files.Columns[2].Width = mode.SelectedIndex == 1 ? 112 : 0;
            RefreshActions();
        }

        private void RefreshActions()
        {
            browse.Enabled = mode.Enabled = recursive.Enabled = files.Enabled = !working;
            scan.Enabled = !working && folder.Text.Length > 0;
            cancel.Enabled = cancellation != null;
            mark.Visible = mode.SelectedIndex == 0;
            mark.Enabled = !working && files.Items.Count > 0 && mode.SelectedIndex == 0;
            compare.Visible = mode.SelectedIndex == 1;
            compare.Enabled = !working && files.SelectedItems.Count == 1;
            var checkedRows = files.CheckedItems.Cast<ListViewItem>().ToList();
            recycle.Enabled = unmark.Enabled = !working && checkedRows.Count > 0;
            checkedInfo.Text = string.Format("Checked for removal: {0}   ({1})", checkedRows.Count,
                SizeText(checkedRows.Sum(r => ((Matches)r.Group.Tag).SizeOf((string)r.Tag))));
        }

        private async Task ScanAsync()
        {
            ResetResults();
            working = true;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            string root = folder.Text;
            bool includeSubfolders = recursive.Checked;
            bool visual = mode.SelectedIndex == 1;
            status.Text = visual ? "Comparing image content across resolutions…" : "Scanning photos and comparing file contents…";
            RefreshActions();
            try
            {
                var found = await Task.Run(() => PhotoScanner.ScanFolder(root, includeSubfolders, visual, token), token);
                token.ThrowIfCancellationRequested();
                bulk = true;
                files.BeginUpdate();
                try
                {
                    int index = 0;
                    foreach (var match in found.Groups)
                    {
                        var group = new ListViewGroup(string.Format("Group {0} · {1} {2}", ++index, match.Paths.Count,
                            visual ? "visually similar photos" : "identical files")) { Tag = match };
                        files.Groups.Add(group);
                        foreach (string path in match.Paths)
                        {
                            var row = new ListViewItem(Path.GetFileName(path), group) { Tag = path, ToolTipText = path };
                            row.SubItems.Add(SizeText(match.SizeOf(path)));
                            row.SubItems.Add(visual ? match.Versions[path].Resolution : "");
                            row.SubItems.Add(Path.GetDirectoryName(path));
                            files.Items.Add(row);
                        }
                    }
                }
                finally { files.EndUpdate(); bulk = false; }
                status.Text = visual
                    ? string.Format("Photos scanned: {0}    Similar groups: {1}    Skipped entries: {2}", found.Photos, found.Groups.Count, found.Skipped)
                    : string.Format("Photos scanned: {0}    Duplicate groups: {1}    Extra copies: {2}    Skipped entries: {3}",
                        found.Photos, found.Groups.Count, found.Groups.Sum(g => g.Paths.Count - 1), found.Skipped);
                if (files.Items.Count > 0) files.Items[0].Selected = true;
            }
            catch (OperationCanceledException) { status.Text = "Scan canceled."; }
            catch (Exception ex) { status.Text = "Scan failed. " + ex.Message; }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                working = false;
                RefreshActions();
            }
        }

        private void OnCheck(object sender, ItemCheckEventArgs e)
        {
            if (e.NewValue != CheckState.Checked || bulk) return;
            var target = files.Items[e.Index];
            if (target.Group.Items.Cast<ListViewItem>().Any(row => row != target && !row.Checked)) return;
            e.NewValue = CheckState.Unchecked;
            status.Text = "Keep at least one file in each group. Uncheck another copy first.";
        }

        private void CheckExtras(bool check)
        {
            bulk = true;
            files.BeginUpdate();
            try
            {
                foreach (ListViewGroup group in files.Groups)
                {
                    var keep = group.Items.Cast<ListViewItem>().First(row => !row.Checked);
                    foreach (ListViewItem row in group.Items) row.Checked = check && row != keep;
                }
            }
            finally { files.EndUpdate(); bulk = false; RefreshActions(); }
        }

        private void SetPicture(Bitmap image)
        {
            var old = picture.Image;
            picture.Image = image;
            if (old != null) old.Dispose();
        }

        private async Task ShowPreviewAsync()
        {
            int version = ++previewVersion;
            SetPicture(null);
            if (files.SelectedItems.Count == 0) { detail.Text = "Select a file to preview it."; return; }
            string path = (string)files.SelectedItems[0].Tag;
            detail.Text = path;
            try
            {
                await Task.Delay(120);
                if (version != previewVersion || IsDisposed) return;
                var image = await Task.Run(() => LoadPreview(path));
                if (version != previewVersion || IsDisposed) image.Dispose();
                else SetPicture(image);
            }
            catch (Exception) { if (version == previewVersion && !IsDisposed) detail.Text = "Preview unavailable." + Environment.NewLine + path; }
        }

        private void CompareSelected()
        {
            if (working || mode.SelectedIndex != 1 || files.SelectedItems.Count == 0) return;
            var row = files.SelectedItems[0];
            using (var dialog = new CompareForm((Matches)row.Group.Tag, (string)row.Tag)) dialog.ShowDialog(this);
        }

        internal static Bitmap LoadPreview(string path)
        {
            using (var input = File.OpenRead(path))
            {
                var frame = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                int orientation = VisualPhoto.ReadOrientation(frame);
                int width = frame.PixelWidth, height = frame.PixelHeight;
                input.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = input;
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (width >= height) image.DecodePixelWidth = Math.Min(800, width);
                else image.DecodePixelHeight = Math.Min(800, height);
                image.EndInit();
                var pixels = new FormatConvertedBitmap(VisualPhoto.Orient(image, orientation), System.Windows.Media.PixelFormats.Bgra32, null, 0);
                var bitmap = new Bitmap(pixels.PixelWidth, pixels.PixelHeight, PixelFormat.Format32bppArgb);
                try
                {
                    var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                    try { pixels.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, checked(data.Stride * data.Height), data.Stride); }
                    finally { bitmap.UnlockBits(data); }
                    return bitmap;
                }
                catch { bitmap.Dispose(); throw; }
            }
        }

        private async Task RecycleAsync()
        {
            var selected = files.CheckedItems.Cast<ListViewItem>().ToList();
            if (selected.Count == 0) return;
            var work = selected.GroupBy(r => r.Group).Select(g => new
            {
                Match = (Matches)g.Key.Tag,
                Keeper = (string)g.Key.Items.Cast<ListViewItem>().First(r => !r.Checked).Tag,
                Copies = g.Select(r => (string)r.Tag).ToList()
            }).ToList();
            string question = string.Format("Move {0} checked file(s) to the Windows Recycle Bin? An unchecked copy will stay in each group.", selected.Count);
            if (mode.SelectedIndex == 1) question += Environment.NewLine + "Visually similar versions may differ in detail.";
            if (MessageBox.Show(this, question, "Review removal", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            working = true;
            RefreshActions();
            status.Text = "Verifying checked files and moving them to the Recycle Bin…";
            var done = new TaskCompletionSource<int>();
            var errors = new List<string>();
            var worker = new Thread(() =>
            {
                try
                {
                    int moved = 0;
                    foreach (var group in work)
                        foreach (string path in group.Copies)
                        {
                            try { PhotoScanner.RecycleCopy(group.Match, group.Keeper, path); moved++; }
                            catch (Exception ex) { errors.Add(path + Environment.NewLine + ex.Message); }
                        }
                    done.SetResult(moved);
                }
                catch (Exception ex) { done.SetException(ex); }
            }) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            int count;
            try { count = await done.Task; }
            finally { working = false; RefreshActions(); }
            if (errors.Count > 0)
                MessageBox.Show(this, string.Format("Moved: {0}. Skipped: {1}.", count, errors.Count) +
                    Environment.NewLine + string.Join(Environment.NewLine, errors.Take(5)), "Removal results", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await ScanAsync();
            status.Text = string.Format("Moved to Recycle Bin: {0}.  ", count) + status.Text;
        }
    }
}
