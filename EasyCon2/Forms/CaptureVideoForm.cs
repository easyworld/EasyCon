﻿using EasyCon2.Capture;
using EasyCon2.Graphic;
using EasyCon2.Helper;
using EasyCon2.Properties;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;

namespace EasyCon2.Forms
{
    public partial class CaptureVideoForm : Form, IDisposable
    {
        public CaptureVideoForm()
        {
            InitializeComponent();
        }

        private enum MonitorMode
        {
            NoBorder = 0,
            Editor = 1,
        }

        private enum SnapshotMode
        {
            NoAction,
            FirstZoom,
            SecondRangeSelect,
            ThridObjSelect,
            Refresh
        }

        readonly string CapDir = Application.StartupPath + "\\Capture\\";
        readonly string ImgDir = Application.StartupPath + "\\ImgLabel\\";

        private bool isMouseDown = false;
        private Point mouseOffset;
        private double monitorScale = 1.1;
        private int monitorHorOrVerZoom = 0;
        private MonitorMode monitorMode = MonitorMode.Editor;

        private Graphics SnapshotGraphic;
        private static Bitmap snapshot;
        private static Bitmap ss;
        private Point SnapshotLMDP = new();
        private Point SnapshotLMMD = new();
        private bool SnapshotLMMing;

        private Point SnapshotRangeMDP = new();
        private Point SnapshotRangeMMP = new();
        private bool SnapshotRangeMove;

        private SnapshotMode snapshotMode = SnapshotMode.NoAction;
        private Point SnapshotPos = new(0, 0);
        private Rectangle SnapshotRangeR = new(0, 0, 0, 0);
        private Rectangle SnapshotSearchObjR = new(0, 0, 0, 0);
        private PointF snapshotScale;

        private ImgLabel curImgLabel = new();
        public static List<ImgLabel> imgLabels = new();

        public int deviceId = -1;
        private readonly OpenCVCapture cvcap = new();

        public CaptureVideoForm(int devId, int typeId)
        {
            InitializeComponent();

            deviceId = devId;
            Debug.WriteLine(deviceId);
            cvcap.CaptureCamera(VideoSourcePlayerMonitor, devId, typeId);
        }

        private void CaptureVideo_Load(object sender, EventArgs e)
        {
            CaptureVideoHelp.Text = Resources.capturedoc;
            if (!Directory.Exists(CapDir))
            {
                Directory.CreateDirectory(CapDir);
            }
            if (!Directory.Exists(ImgDir))
            {
                Directory.CreateDirectory(ImgDir);
            }

            foreach (var method in ImgLabel.GetAllSearchMethod())
            {
                searchMethodComBox.Items.Add(method.ToDescription());
            }

            // data binding
            searchRangX.DataBindings.Add("Text", curImgLabel, "RangeX");
            searchRangY.DataBindings.Add("Text", curImgLabel, "RangeY");
            searchRangW.DataBindings.Add("Text", curImgLabel, "RangeWidth");
            searchRangH.DataBindings.Add("Text", curImgLabel, "RangeHeight");
            targRangX.DataBindings.Add("Text", curImgLabel, "TargetX");
            targRangY.DataBindings.Add("Text", curImgLabel, "TargetY");
            targRangW.DataBindings.Add("Text", curImgLabel, "TargetWidth");
            targRangH.DataBindings.Add("Text", curImgLabel, "TargetHeight");
            lowestMatch.DataBindings.Add("Text", curImgLabel, "matchDegree");

            // load the imglabel
            curImgLabel.SetSource(() => cvcap.GetImage());

            var files = Directory.GetFiles(ImgDir, "*.IL");
            foreach (var file in files)
            {
                try
                {
                    var temp = JsonConvert.DeserializeObject<ImgLabel>(File.ReadAllText(file));
                    if (temp.name == "") continue;
                    temp.Refresh(() => cvcap.GetImage());
                    imgLabels.Add(temp);
                    imgLableList.Items.Add(temp.name);
                }
                catch {/*ignore errors*/ }
            }

            VideoSourcePlayerMonitor.PaintEventHandler += new PaintEventHandler(MonitorPaint);
            Snapshot.PaintEventHandler += new PaintEventHandler(SnapshotPaint);
        }

        private void CaptureVideo_FormClosed(object sender, FormClosedEventArgs e)
        {
            deviceId = -1;
            cvcap.Close();
        }

        private void MonitorPaint(object sender, PaintEventArgs e)
        {
            var resolution = cvcap.CurResolution;
            try
            {
                using var newframe = cvcap.GetImage();
                var g = e.Graphics;
                // Maximize performance
                g.CompositingMode = CompositingMode.SourceOver;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.SmoothingMode = SmoothingMode.None;

                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                // DrawImage() is toooooo SLOW, use DirectX instead PLZ!
                g.DrawImage(newframe, new Rectangle(0, 0, VideoSourcePlayerMonitor.Width, VideoSourcePlayerMonitor.Height), new Rectangle(0, 0, resolution.X, resolution.Y), GraphicsUnit.Pixel);
            }
            catch
            {
                // something wrong, beacause when closing but the render is paintting
            }
        }
        
        private void SnapshotPaint(object sender, PaintEventArgs e)
        {

            if (snapshot == null)
                return;

            SnapshotGraphic = e.Graphics;

            Rectangle delta = new();
            delta.X = SnapshotPos.X - (int)((SnapshotLMMD.X) * snapshotScale.X);
            delta.Y = SnapshotPos.Y - (int)((SnapshotLMMD.Y) * snapshotScale.Y);
            delta.Width = (int)(Snapshot.Width * snapshotScale.X);
            delta.Height = (int)(Snapshot.Height * snapshotScale.Y);
            SnapshotLMMD.X = 0;
            SnapshotLMMD.Y = 0;

            if (snapshotMode != SnapshotMode.NoAction)
            {
                Graphics g = Graphics.FromImage(snapshot);
                g.Clear(Color.FromArgb(240, 240, 240));
                g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);

                if (snapshotMode == SnapshotMode.SecondRangeSelect)
                {
                    // cal the range start pos in bitmap
                    SnapshotRangeR.X = SnapshotPos.X + (int)((SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Y = SnapshotPos.Y + (int)((SnapshotRangeMDP.Y) * snapshotScale.Y);
                    SnapshotRangeR.Width = (int)((SnapshotRangeMMP.X - SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Height = (int)((SnapshotRangeMMP.Y - SnapshotRangeMDP.Y) * snapshotScale.Y);

                    var resolution = cvcap.CurResolution;
                    curImgLabel.RangeX = SnapshotRangeR.X + 2 - resolution.X;
                    curImgLabel.RangeY = SnapshotRangeR.Y + 2 - resolution.Y;
                    curImgLabel.RangeWidth = SnapshotRangeR.Width - 3;
                    curImgLabel.RangeHeight = SnapshotRangeR.Height - 3;
                }

                // range rectangle
                g.DrawRectangle(new Pen(Color.Red, 3), SnapshotRangeR);

                if (snapshotMode == SnapshotMode.ThridObjSelect)
                {
                    // cal the range start pos in bitmap
                    SnapshotSearchObjR.X = SnapshotPos.X + (int)((SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotSearchObjR.Y = SnapshotPos.Y + (int)((SnapshotRangeMDP.Y) * snapshotScale.Y);
                    SnapshotSearchObjR.Width = (int)((SnapshotRangeMMP.X - SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotSearchObjR.Height = (int)((SnapshotRangeMMP.Y - SnapshotRangeMDP.Y) * snapshotScale.Y);

                    var resolution = cvcap.CurResolution;
                    curImgLabel.TargetX = SnapshotSearchObjR.X + 1 - resolution.X;
                    curImgLabel.TargetY = SnapshotSearchObjR.Y + 1 - resolution.Y;
                    curImgLabel.TargetWidth = SnapshotSearchObjR.Width - 2;
                    curImgLabel.TargetHeight = SnapshotSearchObjR.Height - 2;
                }

                // range rectangle
                g.DrawRectangle(new Pen(Color.SpringGreen, 2), SnapshotSearchObjR);
                g.Dispose();
            }

            SnapshotPos.X = delta.X;
            SnapshotPos.Y = delta.Y;

            // draw snapshot
            SnapshotGraphic.DrawImage(snapshot, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), delta, GraphicsUnit.Pixel);
        }

        private void VideoSourcePlayerMonitor_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (monitorMode == MonitorMode.NoBorder)
            {
                // change to editor
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.VideoSourcePlayerMonitor.Dock = DockStyle.None;
                monitorMode = MonitorMode.Editor;
                this.Refresh();
            }
            else if (monitorMode == MonitorMode.Editor)
            {
                // change to noborder
                this.FormBorderStyle = FormBorderStyle.None;
                this.VideoSourcePlayerMonitor.Dock = DockStyle.Fill;
                monitorMode = MonitorMode.NoBorder;
                this.VideoSourcePlayerMonitor.BringToFront();
            }
        }

        private void VideoSourcePlayerMonitor_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.Control)
            {
                monitorHorOrVerZoom = 1;
            }
            else if (e.Shift)
            {
                monitorHorOrVerZoom = 2;
            }
        }

        private void VideoSourcePlayerMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Control control = sender as Control;
                int offsetX = -e.X;
                int offsetY = -e.Y;

                if (!(control is Form))
                {
                    offsetX = offsetX - control.Left;
                    offsetY = offsetY - control.Top;
                }
                mouseOffset = new Point(offsetX, offsetY);
                isMouseDown = true;
                Debug.WriteLine("mouse down" + offsetX + " " + offsetY);
            }
        }

        private void VideoSourcePlayerMonitor_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isMouseDown = false;
                Debug.WriteLine("mouse up");
            }
        }

        private void VideoSourcePlayerMonitor_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown)
            {
                Point mouse = Control.MousePosition;
                mouse.Offset(mouseOffset.X, mouseOffset.Y);
                Debug.WriteLine("mouse move" + mouseOffset.X + " " + mouseOffset.Y);
                this.Location = mouse;
            }
        }

        private void VideoSourcePlayerMonitor_MouseWheel(object sender, MouseEventArgs e)
        {
            if (monitorMode == MonitorMode.Editor)
                return;

            var newSize = new Size(this.Size.Width, this.Size.Height);
            monitorScale = (e.Delta > 0) ? 1.1 : 0.90909;

            Debug.WriteLine(monitorScale.ToString() + " " + newSize.ToString());

            if (monitorHorOrVerZoom == 1)
            {
                newSize.Height = (int)(newSize.Height * monitorScale);
            }
            else if (monitorHorOrVerZoom == 2)
            {
                newSize.Width = (int)(newSize.Width * monitorScale);
            }
            else
            {
                newSize.Width = (int)(newSize.Width * monitorScale);
                newSize.Height = (int)(newSize.Height * monitorScale);
            }

            Debug.WriteLine("new size:" + newSize.ToString());
            monitorHorOrVerZoom = 0;
            this.Size = newSize;
        }

        private void Snapshot_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SnapshotLMDP.X = e.X;
                SnapshotLMDP.Y = e.Y;
                //Debug.WriteLine("donw:"+e.X.ToString()+" "+e.Y.ToString());
                SnapshotLMMing = true;
                Snapshot.Focus();
            }
            else if (e.Button == MouseButtons.Right)
            {
                SnapshotRangeMDP.X = e.X;
                SnapshotRangeMDP.Y = e.Y;
                SnapshotRangeMove = true;
                //Debug.WriteLine("donw:"+e.X.ToString()+" "+e.Y.ToString());
                Snapshot.Focus();
            }
        }

        private void Snapshot_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SnapshotLMMing = false;
            }
            else if (e.Button == MouseButtons.Right)
            {
                SnapshotRangeMove = false;
            }
        }

        private void Snapshot_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SnapshotLMMing)
            {
                Snapshot.Focus();
                SnapshotLMMD.X = e.X - SnapshotLMDP.X;
                SnapshotLMMD.Y = e.Y - SnapshotLMDP.Y;
                SnapshotLMDP.X = e.X;
                SnapshotLMDP.Y = e.Y;
                Snapshot.Refresh();
            }
            else if (e.Button == MouseButtons.Right && SnapshotRangeMove)
            {
                Snapshot.Focus();
                SnapshotRangeMMP.X = e.X;
                SnapshotRangeMMP.Y = e.Y;
                Snapshot.Refresh();
            }
        }

        private void Snapshot_MouseWheel(object sender, MouseEventArgs e)
        {
            Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放
            //var newSize = new Size(this.Size.Width, this.Size.Height);
            snapshotScale.X += (e.Delta > 0) ? -0.1f : 0.1f;
            snapshotScale.Y += (e.Delta > 0) ? -0.1f : 0.1f;

            // limit the scale
            snapshotScale.X = (float)Math.Min(Math.Max(snapshotScale.X, 0.5), 3.0);
            snapshotScale.Y = (float)Math.Min(Math.Max(snapshotScale.Y, 0.5), 3.0);

            Debug.WriteLine("bmpscale:" + snapshotScale.ToString());
            Snapshot.Refresh();
        }

        double max_matchDegree = 0;
        private void searchImg_test()
        {
            Stopwatch sw = new();
            ImgLabel.SearchMethod method;
            if (searchMethodComBox.SelectedItem == null)
                method = ImgLabel.SearchMethod.SqDiffNormed;
            else
                method = EnumHelper.GetEnumFromString<ImgLabel.SearchMethod>(searchMethodComBox.SelectedItem.ToString());

            curImgLabel.searchMethod = method;
            //Debug.WriteLine(method);

            sw.Reset();
            sw.Start();
            var list = curImgLabel.Search(out double matchDegree);
            sw.Stop();

            // load the result
            reasultListBox.Items.Clear();
            if (list.Count > 0)
            {

                for (int i = 0; i < list.Count; i++)
                {
                    reasultListBox.Items.Add(list[i].X.ToString() + "," + list[i].Y.ToString());

                    var result = curImgLabel.getResultImg(i);
                    var g = searchResultImg.CreateGraphics();
                    g.Clear(Color.FromArgb(240, 240, 240));
                    g.DrawImage(result, new Rectangle(0, 0, searchResultImg.Width, searchResultImg.Height), new Rectangle(0, 0, result.Width, result.Height), GraphicsUnit.Pixel);
                    g.Dispose();
                }
                max_matchDegree = Math.Max(matchDegree, max_matchDegree);

                label23.Text = $"匹配度:{matchDegree:f1}%\n" + "耗时:" + sw.ElapsedMilliseconds + "毫秒\n" + $"最大匹配度:{max_matchDegree:f1}%";
            }
            else
            {
                label23.Text = "无法找到目标";
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            searchImg_test();
        }

        private void captureBtn_Click(object sender, EventArgs e)
        {
            // get cur bmp
            ss?.Dispose();
            ss = cvcap.GetImage();
            ss.Save(CapDir + DateTime.Now.Ticks.ToString() + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);

            // need a 9 times of the real pic for display
            snapshot?.Dispose();
            snapshot = new Bitmap(ss.Width * 3, ss.Height * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // draw it at center
            var g = Graphics.FromImage(snapshot);
            g.Clear(Color.FromArgb(240, 240, 240));
            g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);
            g.Dispose();

            // default settings
            SnapshotPos.X = ss.Width;
            SnapshotPos.Y = ss.Height;
            snapshotScale.X = ss.Width / Snapshot.Width;
            snapshotScale.Y = ss.Height / Snapshot.Height;

            // show it
            Snapshot.Refresh();
        }

        private void rangeBtn_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.SecondRangeSelect)
            {
                if (SnapshotRangeR.Width <= 0 || SnapshotRangeR.Height <= 0)
                {
                    MessageBox.Show("搜索范围太小，请重新圈选");
                    return;
                }

                snapshotMode = SnapshotMode.NoAction;
                rangeBtn.Text = "开始圈选(红)";
                targetBtn.Enabled = true;
            }
            else
            {
                snapshotMode = SnapshotMode.SecondRangeSelect;
                rangeBtn.Text = "确定搜索范围";
                targetBtn.Enabled = false;
            }
        }

        private void searchTestBtn_Click(object sender, EventArgs e)
        {
            max_matchDegree = 0;
            targetImg.Image?.Dispose();
            targetImg.Image = curImgLabel.getSearchImg();
            if (targetImg.Image != null)
                searchImg_test();
            else
                MessageBox.Show("没有搜索目标");
        }

        private void targetBtn_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.ThridObjSelect)
            {
                if (SnapshotRangeR.Width <= 0 || SnapshotRangeR.Height <= 0)
                {
                    MessageBox.Show("搜索目标太小，请重新圈选");
                    return;
                }

                Rectangle range = new(SnapshotSearchObjR.X + 1, SnapshotSearchObjR.Y + 1, SnapshotSearchObjR.Width - 2, SnapshotSearchObjR.Height - 2);

                curImgLabel.setSearchImg(snapshot.Clone(range, snapshot.PixelFormat));
                targetImg.Image = curImgLabel.getSearchImg();

                snapshotMode = SnapshotMode.NoAction;
                targetBtn.Text = "开始圈选(绿)";
                rangeBtn.Enabled = true;
            }
            else
            {
                snapshotMode = SnapshotMode.ThridObjSelect;
                targetBtn.Text = "确定搜索目标";
                rangeBtn.Enabled = false;
            }

        }

        private void SaveTagBtn_Click(object sender, EventArgs e)
        {
            ImgLabel.SearchMethod method;
            if (imgLabelNametxt.Text == "")
            {
                MessageBox.Show("搜图标签为空无法保存");
                return;
            }
            if (searchMethodComBox.SelectedItem == null)
                method = ImgLabel.SearchMethod.SqDiffNormed;
            else
                method = EnumHelper.GetEnumFromString<ImgLabel.SearchMethod>(searchMethodComBox.SelectedItem.ToString());

            curImgLabel.searchMethod = method;
            curImgLabel.matchDegree = double.Parse(lowestMatch.Text);

            // save the imglabel to local
            for (int index = 0; index < imgLabels.Count; index++)
            {
                // if the name exist,just overwrite it
                if (imgLabels[index].name == imgLabelNametxt.Text)
                {
                    imgLabels[index].Copy(curImgLabel);
                    imgLabels[index].Save();
                    return;
                }
            }

            // not find, add a new one
            ImgLabel newone = new(() => cvcap.GetImage());
            curImgLabel.name = imgLabelNametxt.Text;

            newone.Copy(curImgLabel);
            newone.Save();

            // add to list and ui
            imgLabels.Add(newone);
            imgLableList.Items.Add(newone.name);
        }

        private void openCapBtn_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new();
            openFileDialog1.Title = "打开";
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.InitialDirectory = Path.GetFullPath(CapDir);
            openFileDialog1.Filter = @"文本文件 (*.bmp)|*.bmp|所有文件 (*.*)|*.*";
            openFileDialog1.FileName = string.Empty;
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;
            Debug.WriteLine(openFileDialog1.FileName);

            // get new snatshot pic
            // get cur bmp
            ss?.Dispose();
            ss = new Bitmap(openFileDialog1.FileName);

            // need a 9 times of the real pic for display
            snapshot?.Dispose();
            snapshot = new Bitmap(ss.Width * 3, ss.Height * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // draw it at center
            Graphics g = Graphics.FromImage(snapshot);
            g.Clear(Color.FromArgb(240, 240, 240));
            g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);
            g.Dispose();

            // default settings
            SnapshotPos.X = ss.Width;
            SnapshotPos.Y = ss.Height;
            snapshotScale.X = ss.Width / Snapshot.Width;
            snapshotScale.Y = ss.Height / Snapshot.Height;

            // show it
            Snapshot.Refresh();
        }

        private void DynTestBtn_Click(object sender, EventArgs e)
        {
            if (DynTestBtn.Text == "动态测试")
            {
                max_matchDegree = 0;
                targetImg.Image?.Dispose();
                targetImg.Image = curImgLabel.getSearchImg();
                if (targetImg.Image != null)
                    searchImg_test();
                else
                {
                    MessageBox.Show("没有搜索目标");
                    return;
                }

                // 60 fps
                timer1.Interval = (int)(1000.0 / 60.0);

                // disable some funcs
                captureBtn.Enabled = false;
                rangeBtn.Enabled = false;
                searchTestBtn.Enabled = false;
                targetBtn.Enabled = false;

                timer1.Start();
                DynTestBtn.Text = "动态测试ing";
            }
            else
            {
                timer1.Stop();
                DynTestBtn.Text = "动态测试";

                captureBtn.Enabled = true;
                rangeBtn.Enabled = true;
                searchTestBtn.Enabled = true;
                targetBtn.Enabled = true;
            }
        }

        private void imgLableList_DoubleClick(object sender, EventArgs e)
        {
            if (imgLableList.SelectedItem != null && imgLableList.SelectedItem.ToString()!= "")
            {
                // load the click item
                foreach (var item in imgLabels)
                {
                    if (item.name == imgLableList.SelectedItem.ToString())
                    {
                        //Debug.WriteLine("find" + item.name);
                        curImgLabel.Copy(item);
                        curImgLabel.Refresh(() => cvcap.GetImage());

                        // update ui
                        imgLabelNametxt.Text = curImgLabel.name;
                        searchMethodComBox.SelectedItem = curImgLabel.searchMethod.ToDescription();
                        lowestMatch.Text = curImgLabel.matchDegree.ToString();
                        targetImg.Image = curImgLabel.getSearchImg();
                        if (targetImg.Image == null)
                            MessageBox.Show("没有搜索目标图片");

                        var resolution = cvcap.CurResolution;
                        SnapshotRangeR.X = curImgLabel.RangeX + resolution.X - 2;
                        SnapshotRangeR.Y = curImgLabel.RangeY + resolution.Y - 2;
                        SnapshotRangeR.Width = curImgLabel.RangeWidth + 3;
                        SnapshotRangeR.Height = curImgLabel.RangeHeight + 3;

                        SnapshotSearchObjR.X = curImgLabel.TargetX + resolution.X - 1;
                        SnapshotSearchObjR.Y = curImgLabel.TargetY + resolution.Y - 1;
                        SnapshotSearchObjR.Width = curImgLabel.TargetWidth + 2;
                        SnapshotSearchObjR.Height = curImgLabel.TargetHeight + 2;

                        snapshotMode = SnapshotMode.Refresh;
                        Snapshot.Refresh();
                    }
                }
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            if (ResolutionBtn.Text == "当前分辨率：1080P")
            {
                // 720p
                cvcap.CurResolution = new Point(1280, 720);
                ResolutionBtn.Text = "当前分辨率：720P";
            }
            else if (ResolutionBtn.Text == "当前分辨率：720P")
            {
                // 480p
                cvcap.CurResolution = new Point(640, 480);
                ResolutionBtn.Text = "当前分辨率：480p";
            }
            else
            {
                // 1080p
                cvcap.CurResolution = new Point(1920, 1080);
                ResolutionBtn.Text = "当前分辨率：1080P";
            }
        }

        private void monitorVisChk_CheckedChanged(object sender, EventArgs e)
        {
            var checkBox = (CheckBox)sender;
            VideoSourcePlayerMonitor.Visible = checkBox.Checked;
        }
    }
}
