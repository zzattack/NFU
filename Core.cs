﻿using Nfu.Models;
using Nfu.Properties;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nfu
{
    public partial class Core : Form
    {
        public bool UpdateAvailable;

        private Image _screenshot;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Core()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Constructor extension.
        /// </summary>
        public void Setup()
        {
            buttonUpdate.FlatStyle = FlatStyle.System;
            Misc.SendMessage(buttonUpdate.Handle, 0x160C, 0, 0xFFFFFFFF);

            if (Settings.Default.HandlePause && !Misc.RegisterHotKey(Keys.Pause, HotKeyPause, 10000, Handle))
            {
                MessageBox.Show(String.Format(Resources.HotKeyNotRegistered, "Pause"),
                    Resources.HotKeyNotRegisteredTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (Settings.Default.HandlePrintScreen && !Misc.RegisterHotKey(Keys.PrintScreen, HotKeyPrintScreen, 20000, Handle))
            {
                MessageBox.Show(String.Format(Resources.HotKeyNotRegistered, "PrintScreen"),
                    Resources.HotKeyNotRegisteredTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Start the update check.
        /// </summary>
        private async void CoreShown(object sender, EventArgs e)
        {
            TaskScheduler taskScheduler = TaskScheduler.FromCurrentSynchronizationContext();

            await Task.Run(() => CheckForUpdate()).ContinueWith(u => {
                if (UpdateAvailable)
                {
                    buttonUpdate.Enabled = true;
                    labelUpdate.Text = Resources.NewVersion;
                    Misc.ShowInfo(Resources.UpdateAvailableTitle, Resources.UpdateAvailable);
                }
                else
                {
                    labelUpdate.Text = Resources.LatestVersion;
                }
            }, taskScheduler);
        }

        /// <summary>
        /// Minimize to system tray.
        /// </summary>
        private void CoreResize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && Settings.Default.MinimizeSystemTray)
            {
                if (!Settings.Default.TooltipShown)
                {
                    Misc.ShowInfo(Resources.StillActiveTitle, Resources.StillActive);
                    Settings.Default.TooltipShown = true;
                }

                Hide();
            }
        }

        /// <summary>
        /// Minimize instead of close.
        /// </summary>
        private void CoreFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
            }
        }

        /// <summary>
        /// Restore window using System Tray.
        /// </summary>
        private void NotifyIconNfuClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (WindowState == FormWindowState.Normal)
            {
                WindowState = FormWindowState.Minimized;
            }
            else
            {
                Show();
                WindowState = FormWindowState.Normal;
            }
        }


        /// <summary>
        /// Handler for the PrintScreen key.
        /// </summary>
        private void HotKeyPrintScreen()
        {
            ButtonScreenshot(null, null);
        }

        /// <summary>
        /// Handler for the Pause key.
        /// </summary>
        private void HotKeyPause()
        {
            ButtonImport(null, null);
        }

        /// <summary>
        /// Select file(s) to upload.
        /// </summary>
        private void ButtonFile(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                Uploader.Upload(openFileDialog.FileNames.Select(fileName => new UploadFile
                {
                    Path = fileName
                }).ToArray());
            }
        }

        /// <summary>
        /// Take a screenshot to upload.
        /// </summary>
        private void ButtonScreenshot(object sender, EventArgs e)
        {
            if (Snipper.IsActive)
                return;

            _screenshot = Snipper.Snip();

            if (_screenshot != null)
            {
                reuploadScreenshotToolStripMenuItem.Enabled = true;

                switch (Snipper.ReturnType)
                {
                    case Snipper.ReturnTypes.Default:
                        Uploader.UploadImage(_screenshot);
                        break;

                    case Snipper.ReturnTypes.ToClipboard:
                        Clipboard.SetImage(_screenshot);
                        Misc.ShowInfo(Resources.ScreenShotCopiedTitle, Resources.ScreenShotCopied);
                        break;
                }
            }
            else
            {
                Misc.HandleError(new ArgumentException(Resources.NoFilesSelected), Resources.ScreenShot);
            }
        }

        /// <summary>
        /// Reupload the last screenshot.
        /// </summary>
        private void ReuploadScreenshot(object sender, EventArgs e)
        {
            Uploader.UploadImage(_screenshot);
        }

        /// <summary>
        /// Import data from the clipboard to upload.
        /// </summary>
        private void ButtonImport(object sender, EventArgs e)
        {
            if (Clipboard.ContainsFileDropList())
            {
                StringCollection files = Clipboard.GetFileDropList();

                Uploader.Upload((from string file in files
                    select new UploadFile
                    {
                        Path = file
                    }).ToArray());
            }
            else if (Clipboard.ContainsImage())
            {
                Uploader.UploadImage(Clipboard.GetImage());
            }
            else if (Clipboard.ContainsText())
            {
                Uploader.UploadText(Clipboard.GetText());
            }
            else
            {
                IDataObject dataObject = Clipboard.GetDataObject();
                if (dataObject != null)
                {
                    Misc.HandleError(new ArgumentException(String.Format(Resources.CannotHandleContentTypes,
                        String.Join(",", dataObject.GetFormats()))), Resources.Import);
                }
                else
                {
                    Misc.HandleError(new ArgumentException(Resources.UnsupportedData), Resources.Import);
                }
            }
        }

        /// <summary>
        /// Open the settings dialog.
        /// </summary>
        private void OpenSettings(object sender, EventArgs e)
        {
            Program.FormCp.ShowDialog();
        }

        /// <summary>
        /// Open the about dialog.
        /// </summary>
        private void OpenAbout(object sender, EventArgs e)
        {
            Program.FormAbout.ShowDialog();
        }

        /// <summary>
        /// Exit NFU.
        /// </summary>
        private void ExitNfu(object sender, EventArgs e)
        {
            Application.Exit();
        }

        /// <summary>
        /// Check for NFU updates.
        /// </summary>
        private async Task CheckForUpdate()
        {
            try
            {
                using (WebClient updateClient = new WebClient())
                {
                    string latestVersion = await updateClient.DownloadStringTaskAsync(new Uri(Settings.Default.VersionUrl));

                    if (latestVersion != Application.ProductVersion)
                    {
                        UpdateAvailable = true;
                    }
                }
            }
            catch (Exception err)
            {
                Misc.HandleError(err, Resources.UpdateCheck);
            }
        }

        /// <summary>
        /// Update NFU.
        /// </summary>
        private void StartUpdate(object sender, EventArgs e)
        {
            try
            {
                string tempNfu = Path.GetTempFileName();
                string tempCmd = Path.GetTempFileName() + ".cmd";

                using (WebClient updateClient = new WebClient())
                {
                    updateClient.DownloadFile(Settings.Default.ExecutableUrl, tempNfu);
                }

                File.WriteAllText(tempCmd, String.Format("@ECHO OFF{3}TITLE {0}{3}ECHO {1}{3}TIMEOUT /T 5{3}ECHO.{3}ECHO {2}{3}COPY /B /Y \"{4}\" \"{5}\"{3}START \"\" \"{5}\"",
                    Resources.UpdateTitle, Resources.WaitingToExit, Resources.Updating, Environment.NewLine, tempNfu, Application.ExecutablePath));

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = tempCmd,
                    Verb = "runas"
                };

                Process.Start(startInfo);

                Application.Exit();
            }
            catch (Exception err)
            {
                Misc.HandleError(err, Resources.Update);
            }
        }
    }
}
