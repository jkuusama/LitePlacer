// Processing tables branch

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Configuration;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Input;
using System.Net;

using MathNet.Numerics;
using HomographyEstimation;

using AForge;
using AForge.Video;
using AForge.Video.DirectShow;
using AForge.Imaging;
using AForge.Imaging.Filters;
using AForge.Math.Geometry;
using Newtonsoft.Json;
using static LitePlacer.CNC;
using System.Net.NetworkInformation;
using System.Windows.Shapes;
//using System.Windows.Controls;

namespace LitePlacer
{
#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CA1308 // Yes, we want to use lower case characters

    /*
    Note: For function success/failure, I use bool return code. (instead of C# exceptions; a philosophical debate, let's not go there too much.
    Still, it should be mentioned that CA1031 is supressed: I think the right way is to tell the user and continue. For example: A save fails; tell the user, 
    Let the user to free room on the disk, plug in a USB stick, whatever, and let the user to try again. 

    The naming convention is xxx_m() for functions that have already displayed an error message to user. If a function only
    calls _m functions, it can consider itself a _m function.
    */

    // =================================================================================
    // Note about thread guards: The prologue "if(InvokeRequired) {something long}" at a start of a function, 
    // makes the function safe to call from another thread.
    // See http://stackoverflow.com/questions/661561/how-to-update-the-gui-from-another-thread-in-c, 
    // "MajesticRa"'s answer near the bottom of first page


    public partial class FormMain : Form
    {
        public CNC Cnc { get; set; }
        public enum ControlBoardType { TinyG, Marlin, other, unknown };

        Camera DownCamera;
        Camera UpCamera;
        NozzleCalibrationClass Nozzle;
        TapesClass Tapes;

        public MySettings Setting;
        public TinyGSettings TinyGBoard { get; set; } = new TinyGSettings();

        // =================================================================================
        // General and "global" functions 
        // =================================================================================
        #region General

        // =================================================================================
        // Thread safe dialog box:
        // (see http://stackoverflow.com/questions/559252/does-messagebox-show-automatically-marshall-to-the-ui-thread )

        public DialogResult ShowMessageBox(String message, String header, MessageBoxButtons buttons)
        {
            if (this.InvokeRequired)
            {
                return (DialogResult)this.Invoke(new PassStringStringReturnDialogResultDelegate(ShowMessageBox), message, header, buttons);
            }
            if (!StartingUp)
            {
                CenteredMessageBox.PrepToCenterMessageBoxOnForm(this);
                return MessageBox.Show(this, message, header, buttons);
            }
            return DialogResult.Cancel;
        }
        public delegate DialogResult PassStringStringReturnDialogResultDelegate(String s1, String s2, MessageBoxButtons buttons);

        // =================================================================================
        // Non-modal dialog box:

        public bool MessageboxDone = false;
        public string NonModalMessageBox_result;   // Pass result trought this

        public string NonModalMessageBox(string message, string header, string str1, string str2, string str3)
        {
            NonModalDialog DialogForm = new NonModalDialog(this, str1, str2, str2);
            // locate to up right corner
            // DialogForm.Parent = FormMain;
            // DialogForm.StartPosition = FormStartPosition.CenterParent;

            // DialogForm.Location = new System.Drawing.Point(this.Location.X + 790, this.Location.Y);

            DialogForm.HeaderString = header;
            DialogForm.MessageString = message;
            DialogForm.Button1Txt = str1;
            DialogForm.Button1Txt = str1;
            DialogForm.Button2Txt = str2;
            DialogForm.Button3Txt = str3;
            MessageboxDone = false;
            DialogForm.Show(this);
            this.Focus();   // get focus back to main screen, saving the user a click

            while (!MessageboxDone)
            {
                Thread.Sleep(1);
                Application.DoEvents();
            }
            return NonModalMessageBox_result;
        }

        // =================================================================================
        // We need "goto" to different features, currently circles, rectangles or both
        public enum FeatureType { Circle, Rectangle, Both };

        public string GetPath()
        {
            return Application.StartupPath + '\\';
        }

        // =================================================================================
        // Startup
        // =================================================================================
        public FormMain()
        {
            Font = new Font(Font.Name, 8.25f * 96f / CreateGraphics().DpiX, Font.Style, Font.Unit, Font.GdiCharSet, Font.GdiVerticalFont);
            InitializeComponent();
            this.MouseWheel += new MouseEventHandler(MouseWheel_event);
        }

        // =================================================================================
        public bool StartingUp { get; set; } = false; // we want to react to some changes, but not during startup data load (which counts as a change)

        // =================================================================================
        private void Form1_Load(object sender, EventArgs e)
        {
            StartingUp = false; // we want the first messages to get through
            DisplayText("Application Start", KnownColor.Black, true);
            DisplayText("Version: " + Assembly.GetEntryAssembly().GetName().Version.ToString() + ", build date: " + BuildDate());
            StartingUp = true;  

            string path = GetPath();
            Setting = new MySettings();
            Setting.MainForm = this;
            Setting = Setting.Load(path + APPLICATIONSETTINGS_DATAFILE);
            Setting.General_SaveFilesAtClosing = true;

            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-us");
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Cnc = new CNC(this);
            CNC.SquareCorrection = Setting.CNC_SquareCorrection;
            DownCamera = new Camera(this, "DownCamera");
            UpCamera = new Camera(this, "UpCamera");
            Nozzle = new NozzleCalibrationClass(UpCamera, Cnc, this);
            Tapes = new TapesClass(Tapes_dataGridView, Nozzle, DownCamera, Cnc, this);

            //Do_Upgrade();

            // Setup error handling for Tapes_dataGridViews
            // This is necessary, because programmatically changing a combobox cell value raises this error. (@MS: booooo!)
            Tapes_dataGridView.DataError += new DataGridViewDataErrorEventHandler(Tapes_dataGridView_DataError);
            TapesOld_dataGridView.DataError += new DataGridViewDataErrorEventHandler(Tapes_dataGridView_DataError);

            this.KeyPreview = true;
            this.KeyDown += new KeyEventHandler(My_KeyDown);
            this.KeyUp += new KeyEventHandler(My_KeyUp);

            Bookmark1_button.Text = Setting.General_Mark1Name;
            Bookmark2_button.Text = Setting.General_Mark2Name;
            Bookmark3_button.Text = Setting.General_Mark3Name;
            Bookmark4_button.Text = Setting.General_Mark4Name;
            Bookmark5_button.Text = Setting.General_Mark5Name;
            Bookmark6_button.Text = Setting.General_Mark6Name;
            Mark1_textBox.Text = Setting.General_Mark1Name;
            Mark2_textBox.Text = Setting.General_Mark2Name;
            Mark3_textBox.Text = Setting.General_Mark3Name;
            Mark4_textBox.Text = Setting.General_Mark4Name;
            Mark5_textBox.Text = Setting.General_Mark5Name;
            Mark6_textBox.Text = Setting.General_Mark6Name;

            VigorousHoming_checkBox.Checked = Setting.General_VigorousHoming;

            Relative_Button.Checked = true;

            NozzleHeightInstructions_label.Text = "";

        }

        // ==============================================================================================
        private void FormMain_Shown(object sender, EventArgs e)
        {
            // ======== General form setup:

            string path = GetPath();

            // For ease of design, the picture box is drawn on a setup Cameras tab. 
            // We want it to be owned by main form (not by tab) and visible when needed.
            // Position math: https://stackoverflow.com/questions/1478022/c-sharp-get-a-controls-position-on-a-form
            System.Drawing.Point locationOnForm =
                Cam_pictureBox.FindForm().PointToClient(Cam_pictureBox.Parent.PointToScreen(Cam_pictureBox.Location));
            Cam_pictureBox.Parent = this;                   // position changes
            Cam_pictureBox.Location = locationOnForm;       // move it back

            // At design time, I can't draw items on top of each other. I draw them at a convenient location; this
            // moves motor control boxes to correct place
            TinyGMotors_tabControl.Location = new System.Drawing.Point(6, 191);
            MarlinMotors_tabControl.Location = new System.Drawing.Point(6, 191);

            labelSerialPortStatus.ForeColor = Color.Red;
            labelSerialPortStatus.Text = "Starting up";

            LabelTestButtons();
            AttachHelpHandlers(this.Controls);

            if (!CreateDataBackups())
            {
                Environment.Exit(0);
            }

            BasicSetupTab_Begin();      // Form comes up with basic setup tab, but the tab change event doesn't fire


            if (Setting.Nozzles_current == 0)
            {
                NozzleNo_textBox.Text = "--";
            }
            else
            {
                NozzleNo_textBox.Text = Setting.Nozzles_current.ToString(CultureInfo.InvariantCulture);
            }
            ForceNozzle_numericUpDown.Value = Setting.Nozzles_default;
            DefaultNozzle_label.Text = Setting.Nozzles_default.ToString(CultureInfo.InvariantCulture);

            // ======== Setup Video Processing tab:  (needs to be first, other pages need the algorithms info)

            InitVideoAlgorithmsUI();
            // before the feature is fully implemented, hide the stored images tab. 
            // AdvancedProcessing_tabControl.TabPages.Remove(StoredImages_tabPage);

            // ======== Run Job tab:

            OmitNozzleCalibration_checkBox.Checked = Setting.Placement_OmitNozzleCalibration;
            SkipMeasurements_checkBox.Checked = Setting.Placement_SkipMeasurements;
            JobOffsetX_textBox.Text = Setting.Job_Xoffset.ToString("0.000", CultureInfo.InvariantCulture);
            JobOffsetY_textBox.Text = Setting.Job_Yoffset.ToString("0.000", CultureInfo.InvariantCulture);

            this.Refresh(); // waits until all widgets have loaded
            LoadTempCADdata();
            LoadTempJobData();

            // ======== Basic Setup tab:

            CheckForUpdate_checkBox.Checked = Setting.General_CheckForUpdates;
            if (CheckForUpdate_checkBox.Checked)
            {
                CheckForUpdate();
            }
            // ======== Setup Cameras tab:

            RobustFast_checkBox.Checked = Setting.Cameras_RobustSwitch;
            KeepActive_checkBox.Checked = Setting.Cameras_KeepActive;
            if (KeepActive_checkBox.Checked)
            {
                RobustFast_checkBox.Enabled = false;
            }
            else
            {
                RobustFast_checkBox.Enabled = true;
            }
            RobustFast_checkBox.Checked = Setting.Cameras_RobustSwitch;
            DownCamMaxResolution_checkBox.Checked = Setting.DownCam_UseMaxResolution;
            DoDownCamMaxResolution_checkBox_CheckedChanged();
            UpCamMaxResolution_checkBox.Checked = Setting.UpCam_UseMaxResolution;
            DoUpCamMaxResolution_checkBox_CheckedChanged();

            DownCamera.ImageBox = Cam_pictureBox;
            DownCamera.DrawCross = Setting.DownCam_DrawCross;
            DownCamera.DrawBox = Setting.DownCam_DrawBox;
            DownCamera.DrawSidemarks = Setting.DownCam_DrawSidemarks;
            DownCamera.DesiredResolutionX = Setting.DownCam_DesiredX;
            DownCamera.DesiredResolutionY = Setting.DownCam_DesiredY;
            DownCamera.XmmPerPixel = Setting.DownCam_XmmPerPixel;
            DownCamera.YmmPerPixel = Setting.DownCam_YmmPerPixel;
            DownCamZoom_checkBox.Checked = Setting.DownCam_Zoom;
            DownCamera.ZoomIsOn = Setting.DownCam_Zoom;
            DownCamera.ZoomFactor = Setting.DownCam_Zoomfactor;
            DowncamMirror_checkBox.Checked = Setting.Downcam_Mirror;

            UpCamera.ImageBox = Cam_pictureBox;
            UpCamera.DrawCross = Setting.UpCam_DrawCross;
            UpCamera.DrawBox = Setting.UpCam_DrawBox;
            UpCamera.DrawSidemarks = Setting.UpCam_DrawSidemarks;
            UpCamera.DesiredResolutionX = Setting.UpCam_DesiredX;
            UpCamera.DesiredResolutionY = Setting.UpCam_DesiredY;
            UpCamera.XmmPerPixel = Setting.UpCam_XmmPerPixel;
            UpCamera.YmmPerPixel = Setting.UpCam_YmmPerPixel;
            UpCamZoom_checkBox.Checked = Setting.UpCam_Zoom;
            UpCamera.ZoomIsOn = Setting.UpCam_Zoom;
            UpCamera.ZoomFactor = Setting.UpCam_Zoomfactor;
            UpcamMirror_checkBox.Checked = Setting.Upcam_Mirror;


            ShowPixels_checkBox.Checked = Setting.Cam_ShowPixels;

            StartCameras();

            UpCamZoomFactor_textBox.Text = Setting.UpCam_Zoomfactor.ToString("0.0", CultureInfo.InvariantCulture);
            DownCamZoomFactor_textBox.Text = Setting.DownCam_Zoomfactor.ToString("0.0", CultureInfo.InvariantCulture);
            DownCameraXmmPerPixel_textBox.Text = Setting.DownCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
            UpdateDownCamBoxXSizeText();
            DownCameraYmmPerPixel_textBox.Text = Setting.DownCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
            UpdateDownCamBoxYSizeText();
            UpCameraXmmPerPixel_textBox.Text = Setting.UpCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
            UpdateUpCamBoxXSizeText();
            UpCameraYmmPerPixel_textBox.Text = Setting.UpCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
            UpdateUpCamBoxYSizeText();

            // ======== Setup vision processing tab:
            NoOfNozzlesOnVideoSetup_numericUpDown.Maximum = Setting.Nozzles_count;
            InitStoredImages();

            // ======== Tape Positions tab:

            LoadTapesTable(path + TAPES_DATAFILE);

            // ======== Nozzles Setup tab:

            Nozzles_initialize();

            // ======== Setup operations that can cause visible reaction:

            PositionConfidence = false;
            OpticalHome_button.BackColor = Color.Red;
            StartingUp = false;
            ConnectToCnc(Setting.CNC_SerialPort);  // This can raise error condition, needing the form up

            MotorPower_timer.Enabled = true;
            DisableLog_checkBox.Checked = Setting.General_MuteLogging;
            DisplayText("Startup completed.");
        }

        // =================================================================================
        // Close
        // =================================================================================
        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            Setting.CNC_EnableMouseWheelJog = MouseScroll_checkBox.Checked;
            Setting.CNC_EnableNumPadJog = NumPadJog_checkBox.Checked;
            Setting.General_CheckForUpdates = CheckForUpdate_checkBox.Checked;
            Setting.General_MuteLogging = DisableLog_checkBox.Checked;

            if (!SaveAllData())
            {
                e.Cancel = true;
                return;
            }

            if (Cnc.Connected)
            {
                Cnc.PumpIsOn = true;        // so it will be turned off, no matter what we think the status
                PumpOff();
                Cnc.VacuumDefaultSetting();
                Cnc.MotorPowerOff();
                Cnc.EnableZswitches();
            }
            Cnc.ClosePort();

            if (DownCamera.IsRunning())
            {
                DownCamera.Close();
            }
            if (UpCamera.IsRunning())
            {
                UpCamera.Close();
            }
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(2);
                Application.DoEvents();
            }
            Environment.Exit(0);    // kills all processes and threads (solves exit during startup issue)
        }

        // ==============================================================================================
        // Save and load data
        // =================================================================================

        // =================================================================================
        // File names
        public const string VIDEOALGORITHMS_DATAFILE = "LitePlacer.VideoAlgorithms";
        public const string APPLICATIONSETTINGS_DATAFILE = "LitePlacer.Appsettings";
        public const string TAPES_DATAFILE = "LitePlacer.TapesData_v2";
        public const string NOZZLES_CALIBRATION_DATAFILE = "LitePlacer.NozzlesCalibrationData_v3";
        public const string NOZZLES_LOAD_DATAFILE = "LitePlacer.NozzlesLoadData_v2";
        public const string NOZZLES_UNLOAD_DATAFILE = "LitePlacer.NozzlesUnLoadData_v2";
        public const string NOZZLES_VISIONPARAMETERS_DATAFILE = "LitePlacer.NozzlesVisionParameters_v21";
        public const string BOARDSETTINGS_DATAFILE = "LitePlacer.BoardSettings";
        public const string BACKUP_DIRNAME = "DataBackup";

        private List<string> DataFiles = new List<string> {
            VIDEOALGORITHMS_DATAFILE, APPLICATIONSETTINGS_DATAFILE, TAPES_DATAFILE,
            NOZZLES_CALIBRATION_DATAFILE, NOZZLES_LOAD_DATAFILE, NOZZLES_UNLOAD_DATAFILE,
            NOZZLES_VISIONPARAMETERS_DATAFILE, BOARDSETTINGS_DATAFILE};


        private bool CreateDataBackups()
        {
            string FilesPath = GetPath();
            string BackupsPath = FilesPath + BACKUP_DIRNAME + "\\" + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            DisplayText("Creating data file backups to "+ BackupsPath);
            try
            {
                // If backup directory doesn't exist, create it. CreateDirectory() does the check automatically
                Directory.CreateDirectory(BackupsPath);
                foreach (var fName in DataFiles)
                {
                    // On first runs, not all data files exist. that is not an error
                    if (File.Exists(fName))
                    {
                        File.Copy(System.IO.Path.Combine(FilesPath, fName), System.IO.Path.Combine(BackupsPath, fName), true);
                    }
                }
                return true;
            }
            catch (System.Exception excep)
            {
                DisplayText(excep.Message);
                return false;
            }

        }


        private void SaveAlldata_button_Click(object sender, EventArgs e)
        {
            // save to current directory
            if (!SaveAllData())
            {
                return;
            }
            // and copy to backup directory
            CreateDataBackups();
        }

        private bool SaveAllData()
        {
            bool OK = true;
            bool res;
            if (Setting.General_SaveFilesAtClosing)
            {
                string path = GetPath();

                res = SaveTempCADdata();
                OK = OK && res;

                res = SaveTempJobData();
                OK = OK && res;

                res = Setting.Save(Setting, path + APPLICATIONSETTINGS_DATAFILE);
                OK = OK && res;

                res = SaveDataGrid(path + TAPES_DATAFILE, Tapes_dataGridView);
                OK = OK && res;

                res = SaveDataGrid(path + NOZZLES_LOAD_DATAFILE, NozzlesLoad_dataGridView);
                OK = OK && res;

                res = SaveDataGrid(path + NOZZLES_UNLOAD_DATAFILE, NozzlesUnload_dataGridView);
                OK = OK && res;

                res = SaveDataGrid(path + NOZZLES_VISIONPARAMETERS_DATAFILE, NozzlesParameters_dataGridView);
                OK = OK && res;

                res = Nozzle.SaveNozzlesCalibration(path + NOZZLES_CALIBRATION_DATAFILE);
                OK = OK && res;

                res = SaveVideoAlgorithms(path + VIDEOALGORITHMS_DATAFILE, VideoAlgorithms);
                OK = OK && res;

                if (!OK)
                {
                    DialogResult dialogResult = ShowMessageBox(
                        "Some data could not be saved (see log window). Continue?",
                        "Data save problem", MessageBoxButtons.YesNo);
                    if (dialogResult == DialogResult.No)
                    {
                        return false;
                    }
                }
            }
            return true;
        }


        // ==============================================================================================
        // New software release checks

        private string BuildDate()
        {
            // see http://stackoverflow.com/questions/1600962/displaying-the-build-date
            var version = Assembly.GetEntryAssembly().GetName().Version;
            var buildDateTime = new DateTime(2000, 1, 1).Add(new TimeSpan(
            TimeSpan.TicksPerDay * version.Build + // days since 1 January 2000
            TimeSpan.TicksPerSecond * 2 * version.Revision)); // seconds since midnight, (multiply by 2 to get original)
            return buildDateTime.ToString(CultureInfo.InvariantCulture);
        }

        private bool UpdateAvailable(out string WebDate, out string ThisDate)
        {
            try
            {
                var url = "http://www.liteplacer.com/Downloads/release.txt";
                string DateFromWeb = (new WebClient()).DownloadString(url);
                WebDate = "";
                for (int i = 0; i < DateFromWeb.Length; i++)
                {
                    if (DateFromWeb[i] == '\n')
                    {
                        break;
                    }
                    WebDate += DateFromWeb[i];
                }
                WebDate = WebDate.Trim();  // UpdateDate is the date on the web, format "Build date 08/29/2021"
                WebDate = WebDate.Remove(0, "Build date ".Length);
                ThisDate = BuildDate();
                ThisDate = BuildDate().Substring(0, ThisDate.IndexOf(' '));
                if (WebDate != ThisDate)
                {
                    return true;
                }
            }
            catch
            {
                DisplayText("Could not read http://www.liteplacer.com/Downloads/release.txt, update info not available.");
                WebDate = "";
                ThisDate = "";
                return false;
            }
            return false;
        }

        private bool CheckForUpdate()
        {
            string WebDate;
            string ThisDate;
            if (UpdateAvailable(out WebDate, out ThisDate))
            {
                ShowMessageBox(
                    "There is a software update available (or you are a running pre-release).\n\r" +
                    "Date of software on web: " + WebDate + "\n\r" +
                    "Date of this software: " + ThisDate + "\n\r",
                    "Update available",
                    MessageBoxButtons.OK);  ;
                return true;
            }
            return false;
        }

        private void CheckNow_button_Click(object sender, EventArgs e)
        {
            if (!CheckForUpdate())
            {
                ShowMessageBox(
                    "The software is up to date",
                    "Up to date",
                    MessageBoxButtons.OK);
            }
        }

        // ==============================================================================================
        // For diagnostics, log button presses so that the button click appears in log window contents.
        // but the window does not log user actions without this.)
        // Also, keep track of which control the mouse points at (mouseEnter), so that pressing F1
        // for help can find help URL from tag
        // https://stackoverflow.com/questions/7545775/how-to-loop-through-all-controls-in-a-windows-forms-form-or-how-to-find-if-a-par

        public void AttachHelpHandlers(System.Windows.Forms.Control.ControlCollection controls)
        {
            foreach (var control in controls.Cast<System.Windows.Forms.Control>())
            {
                if (control is Button)
                {
                    Button button = (Button)control;
                    button.MouseDown += LogButtonClick; // MouseDown comes before mouse click, we want this to fire first)
                }
                control.MouseEnter += TrackMouseEnter;
                AttachHelpHandlers(control.Controls);
            }
        }

        private void LogButtonClick(object sender, EventArgs eventArgs)
        {

            Button button = sender as Button;
            DisplayText("B: " + button.Text.ToString(CultureInfo.InvariantCulture), KnownColor.DarkGreen, true);
        }


        public string LastTag;
        private void TrackMouseEnter(object sender, EventArgs eventArgs)
        {
            System.Windows.Forms.Control control = sender as System.Windows.Forms.Control;
            if (control.Tag != null)
            {
                LastTag = control.Tag.ToString();
            }
            else
            {
                LastTag = "";
            }
        }

        // ==============================================================================================
        // =================================================================================
        // Get and save settings from old version if necessary
        // http://blog.johnsworkshop.net/automatically-upgrading-user-settings-after-an-application-version-change/
        /*
        private void Do_Upgrade()
        {
            try
            {
                if (Setting.General_UpgradeRequired)
                {
                    DisplayText("Updating from previous version");
                    Setting.Upgrade();
                    Setting.General_UpgradeRequired = false;
                    Setting.Save();
                }
            }
            catch (SettingsPropertyNotFoundException)
            {
                DisplayText("Updating from previous version (through ex)");
                Setting.Upgrade();
                Setting.General_UpgradeRequired = false;
                Setting.Save();
            }

        }
        */
        // =================================================================================

        public string LastTabPage = "";

        private void tabControlPages_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (tabControlPages.SelectedTab.Name)
            {
                case "RunJob_tabPage":
                    Cam_pictureBox.BringToFront();
                    Cam_pictureBox.Visible = true;
                    RunJob_tabPage_Begin();
                    LastTabPage = "RunJob_tabPage";
                    break;
                case "tabPageBasicSetup":
                    Cam_pictureBox.Visible = false;
                    BasicSetupTab_Begin();
                    LastTabPage = "tabPageBasicSetup";
                    break;
                case "tabPageSetupCameras":
                    Cam_pictureBox.BringToFront();
                    Cam_pictureBox.Visible = true;
                    tabPageSetupCameras_Begin();
                    LastTabPage = "tabPageSetupCameras";
                    break;
                case "Algorithms_tabPage":
                    Cam_pictureBox.BringToFront();
                    Cam_pictureBox.Visible = true;
                    Algorithms_tabPage_Begin();
                    LastTabPage = "Algorithms_tabPage";
                    break;
                case "Tapes_tabPage":
                    Cam_pictureBox.BringToFront();
                    Cam_pictureBox.Visible = true;
                    Tapes_tabPage_Begin();
                    LastTabPage = "Tapes_tabPage";
                    break;
                case "Nozzles_tabPage":
                    Cam_pictureBox.Visible = false;
                    Nozzles_tabPage_Begin();
                    LastTabPage = "Nozzles_tabPage";
                    break;
            }
        }

        private void tabControlPages_Selecting(object sender, TabControlCancelEventArgs e)
        {
            if (StartingUp)
            {
                e.Cancel = true;
                return;
            };

            switch (LastTabPage)
            {
                case "RunJob_tabPage":
                    RunJob_tabPage_End();
                    break;
                case "tabPageBasicSetup":
                    BasicSetupTab_End();
                    break;
                case "tabPageSetupCameras":
                    tabPageSetupCameras_End();
                    break;
                case "Algorithms_tabPage":
                    Algorithms_tabPage_End();
                    break;
                case "Tapes_tabPage":
                    Tapes_tabPage_End();
                    break;
                case "Nozzles_tabPage":
                    Nozzles_tabPage_End();
                    break;
            }
        }

        bool DowncamAciveSafe = false;
        bool UpcamAciveSafe = false;
        // This happens before anything else when changing tabs. Pause cameras for during change
        private void tabControlPages_Deselecting(object sender, TabControlCancelEventArgs e)
        {
            DowncamAciveSafe = DownCamera.Active;
            UpcamAciveSafe = UpCamera.Active;
        }

        // And this happens last. Activate cameras again
        private void tabControlPages_Selected(object sender, TabControlEventArgs e)
        {
            DownCamera.Active = DowncamAciveSafe;
            UpCamera.Active = UpcamAciveSafe;
        }

        // =================================================================================
        // Saving and restoring data tables (Note: Not job files)
        // =================================================================================

        // Reading ver2 format allows changing the data grid itself at a software update, 
        // adding and removing columns, and still read in a saved file from previous software version.

        public enum DataTableType { Tapes, VideoProcessing, PanelFiducials, Nozzles };

        private int Ver2FormatID = 20000001;  // Just in case we need to identify the format we are using. 
        public bool LoadingDataGrid = false;  // to avoid problems with cell value changed event and unfilled grids


        public void LoadDataGrid(string FileName, DataGridView dgv, DataTableType TableType)
        {
            try
            {
                bool Ver2 = false;
                LoadingDataGrid = true;
                int first;


                if (File.Exists(FileName + "_v2"))
                {
                    FileName = FileName + "_v2";
                }
                else
                {
                    if (!File.Exists(FileName))
                    {
                        DisplayText("Didn't find file " + FileName);
                        return;   // Didint find the specified file name nor filename+v2 (these would be the default files)
                    }
                }

                // Find out the version
                using (BinaryReader br1 = new BinaryReader(File.Open(FileName, FileMode.Open)))
                {
                    first = br1.ReadInt32();
                    if (first == Ver2FormatID)
                    {
                        Ver2 = true;
                    }
                    br1.Close();
                }


                using (BinaryReader br = new BinaryReader(File.Open(FileName, FileMode.Open)))
                {
                    dgv.Rows.Clear();
                    if (Ver2)
                    {
                        DisplayText("Reading v2 format file " + FileName);
                        first = br.ReadInt32();
                    }
                    else
                    {
                        DisplayText("Reading v1 format file " + FileName);
                    }

                    int cols = br.ReadInt32();
                    int rows = br.ReadInt32();

                    if (dgv.AllowUserToAddRows)
                    {
                        // There is an empty row in the bottom that is visible for manual add.
                        // It is saved in the file. It is automatically added, so we don't want to add it again.
                        rows = rows - 1;
                    }
                    // read headers;
                    List<string> Headers = new List<string>();

                    if (Ver2)
                    {
                        for (int j = 0; j < cols; ++j)
                        {
                            Headers.Add(br.ReadString());
                        }
                    }
                    else
                    {
                        Headers = Addv1Headers(TableType);
                    }

                    // read data
                    int i_out;
                    for (int i = 0; i < rows; ++i)
                    {
                        dgv.Rows.Add();
                        for (int j = 0; j < cols; ++j)
                        {
                            if (MapHeaders(dgv, Headers, j, out i_out))
                            {
                                if (br.ReadBoolean())
                                {
                                    dgv.Rows[i].Cells[i_out].Value = br.ReadString();
                                }
                                else
                                {
                                    br.ReadBoolean();
                                }
                            }
                            else
                            {
                                // column is removed: dummy read, discard the data
                                if (br.ReadBoolean())
                                {
                                    br.ReadString();
                                }
                                else br.ReadBoolean();
                            }
                        }
                    }
                    br.Close();
                }
                LoadingDataGrid = false;
            }
            catch (System.Exception excep)
            {
                MessageBox.Show(excep.Message);
                LoadingDataGrid = false;
            }
        }

        private bool MapHeaders(DataGridView Grid, List<string> Headers, int i_in, out int i_out)
        {
            // i_in= column index of the read-in data
            // sets i_out to the column index of the currect header in grid
            // returns if match was found
            i_out = -1;
            string label = Headers[i_in];
            for (int i = 0; i < Grid.Columns.Count; i++)
            {
                if (Grid.Columns[i].Name == label)
                {
                    i_out = i;
                    return true;
                }
            }
            return false;
        }



        public bool SaveDataGrid(string FileName, DataGridView dgv)
        {
            try
            {
                DisplayText("Saving "+ FileName);
                using (BinaryWriter bw = new BinaryWriter(File.Open(FileName, FileMode.Create)))
                {
                    bw.Write(Ver2FormatID);
                    bw.Write(dgv.Columns.Count);
                    bw.Write(dgv.Rows.Count);
                    LoadingDataGrid = false;

                    // write headers
                    foreach (DataGridViewColumn column in dgv.Columns)
                    {
                        bw.Write(column.Name);
                    }

                    int loopcount = 0;
                    foreach (DataGridViewRow dgvR in dgv.Rows)
                    {
                        loopcount++;
                        for (int j = 0; j < dgv.Columns.Count; ++j)
                        {
                            object val = dgvR.Cells[j].Value;
                            if (val == null)
                            {
                                bw.Write(false);
                                bw.Write(false);
                            }
                            else
                            {
                                bw.Write(true);
                                bw.Write(val.ToString());
                            }
                        }
                    }
                    bw.Flush();
                    // bw.Close();
                }
                return true;
            }
            catch (System.Exception excep)
            {
                DisplayText(excep.Message);
                return false;
            }
        }

        // =================================================================================
        // To be able to change columns and read in old format data file, we need to manually set the old
        // format headers, since I didn't have the insight to write them to the file from beginning.
        // this routine does it, called from LoadDataGrid()

        public List<string> Addv1Headers(DataTableType TableType)
        {
            List<string> Headers = new List<string>();

            switch (TableType)
            {
                //case DataTableType.Nozzles:
                //    Headers.Add("NozzleNo_Column");
                //    Headers.Add("StartX_Column");
                //    Headers.Add("StartY_Column");
                //    Headers.Add("StartZ_Column");
                //    for (int i = 1; i < 6; i++)
                //    {
                //        Headers.Add("MoveNumber" + i.ToString() + "axis_Column");
                //        Headers.Add("MoveNumber" + i.ToString() + "amount_Column");
                //    }
                //    break;

                case DataTableType.Tapes:
                    Headers.Add("SelectButton_Column");
                    Headers.Add("Id_Column");
                    Headers.Add("Orientation_Column");
                    Headers.Add("Rotation_Column");
                    Headers.Add("WidthColumn");
                    Headers.Add("Type_Column");
                    // Headers.Add("CapacityColumn");   // not in v1 files
                    Headers.Add("NextPart_Column");
                    Headers.Add("TrayID_Column");
                    Headers.Add("FirstX_Column");
                    Headers.Add("FirstY_Column");
                    Headers.Add("Z_Pickup_Column");
                    Headers.Add("Z_Place_Column");
                    Headers.Add("Next_X_Column");
                    Headers.Add("Next_Y_Column");
                    break;

                case DataTableType.VideoProcessing:
                    Headers.Add("Funct_column");
                    Headers.Add("Enabled_column");
                    Headers.Add("Int1_column");
                    Headers.Add("Double1_column");
                    Headers.Add("R_column");
                    Headers.Add("G_column");
                    Headers.Add("B_column");
                    break;

                case DataTableType.PanelFiducials:
                    Headers.Add("Designator_Column");
                    Headers.Add("Footprint_Column");
                    Headers.Add("FirstX_Column");
                    Headers.Add("FirstY_Column");
                    Headers.Add("Rotation_Column");
                    break;



                default:
                    ShowMessageBox(
                        "*** Header description for " + TableType.ToString() + " missing. Programmer's error. ***",
                        "Sloppy programmer",
                        MessageBoxButtons.OK);
                    break;
            }

            return Headers;
        }

        // =================================================================================
        // This routine reads in old format file
        public void LoadDataGrid_V1(string FileName, DataGridView dgv)
        {
            try
            {
                if (!File.Exists(FileName))
                {
                    return;
                }
                LoadingDataGrid = true;
                dgv.Rows.Clear();
                using (BinaryReader bw = new BinaryReader(File.Open(FileName, FileMode.Open)))
                {
                    int Cols = bw.ReadInt32();
                    int Rows = bw.ReadInt32();
                    string debug = "foo";
                    if (dgv.AllowUserToAddRows)
                    {
                        // There is an empty row in the bottom that is visible for manual add.
                        // It is saved in the file. It is automatically added, so we don't want to add it also.
                        // It is not there when rows are added only programmatically, so we need to do it here.
                        Rows = Rows - 1;
                    }
                    for (int i = 0; i < Rows; ++i)
                    {
                        dgv.Rows.Add();
                        for (int j = 0; j < Cols; ++j)
                        {
                            if (bw.ReadBoolean())
                            {
                                debug = bw.ReadString();
                                dgv.Rows[i].Cells[j].Value = "";
                                dgv.Rows[i].Cells[j].Value = debug;
                            }
                            else bw.ReadBoolean();
                            if (dgv.Rows[i].Cells[j].Value == null)
                            {
                                dgv.Rows[i].Cells[j].Value = "--";
                            }
                            if (string.IsNullOrEmpty(dgv.Rows[i].Cells[j].Value.ToString()))
                            {
                                dgv.Rows[i].Cells[j].Value = "--";
                            }
                        }
                    }
                    //bw.ClosePort();
                }
                LoadingDataGrid = false;
            }
            catch (System.Exception excep)
            {
                MessageBox.Show(excep.Message);
                LoadingDataGrid = false;
            }
        }


        // =================================================================================
        // I changed the data in tapes table, but I don't want to make customers to redo their tables
        // This routine looks if the data format is old and converts it to new if needed
        // This is called at startup
        private void LoadTapesTable(string filename)
        {
            if (!File.Exists(filename))
            {
                DisplayText("Did not find file " + filename);
                return;
            }

            if (filename.EndsWith("_v2", StringComparison.Ordinal))
            {
                ReadV2TapesFile(filename);
            }
            else
            {
                ReadV1TapesFile(filename);
            }
            return;
        }

        private void ReadV2TapesFile(string filename)
        {
            // Logic:
            // Check that file exists.
            // Read the headers.
            // If headers have "WidthColumn", read in to dummy datagridview and convert to new format
            // else read normally
            try
            {
                using (BinaryReader br = new BinaryReader(File.Open(filename, FileMode.Open)))
                {
                    int first = br.ReadInt32();
                    int cols = br.ReadInt32();
                    int rows = br.ReadInt32();
                    // read headers;
                    List<string> Headers = new List<string>();
                    for (int j = 0; j < cols; ++j)
                    {
                        Headers.Add(br.ReadString());
                    }
                    br.Close();
                    if (Headers.Contains("WidthColumn"))
                    {
                        // read in to old format datagridview and convert to new format
                        DisplayText("Loading tapes without nozzles data");
                        LoadDataGrid(filename, TapesOld_dataGridView, DataTableType.Tapes);
                        // convert to new
                        double X;
                        double Y;
                        double pitch;
                        LoadingDataGrid = true;  // to avoid cell value changed events
                        Tapes_dataGridView.Rows.Clear();

                        for (int i = 0; i < TapesOld_dataGridView.RowCount; i++)
                        {
                            Tapes_dataGridView.Rows.Add();

                            // most values go as is, just column names have changed
                            if (TapesOld_dataGridView.Rows[i].Cells["SelectButtonColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["SelectButton_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["SelectButtonColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["IdColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Id_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["IdColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["OrientationColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Orientation_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["OrientationColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["RotationColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Rotation_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["RotationColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["NozzleColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Nozzle_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["NozzleColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["CapacityColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Capacity_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["CapacityColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["TypeColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["TypeColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["Tray_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["TrayID_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["Tray_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["Next_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["NextPart_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["Next_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["X_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["FirstX_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["X_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["Y_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["FirstY_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["Y_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["PickupZ_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Z_Pickup_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["PickupZ_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["PlaceZ_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Z_Place_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["PlaceZ_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["NextX_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Next_X_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["NextX_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["NextY_column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Next_Y_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["NextY_column"].Value.ToString();
                            }
                            // conversion
                            if (TapesOld_dataGridView.Rows[i].Cells["WidthColumn"].Value != null)
                            {
                                if (TapesOld_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString() != "custom")
                                {
                                    TapeWidthStringToValues(TapesOld_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString(), out X, out Y, out pitch);
                                    Tapes_dataGridView.Rows[i].Cells["Pitch_Column"].Value = pitch.ToString(CultureInfo.InvariantCulture);
                                    Tapes_dataGridView.Rows[i].Cells["OffsetX_Column"].Value = X.ToString(CultureInfo.InvariantCulture);
                                    Tapes_dataGridView.Rows[i].Cells["OffsetY_Column"].Value = Y.ToString(CultureInfo.InvariantCulture);
                                }
                            }
                            Tapes_dataGridView.Rows[i].Cells["RotationDirect_Column"].Value = "0.00";
                            Tapes_dataGridView.Rows[i].Cells["CoordinatesForParts_Column"].Value = false;
                            Tapes_dataGridView.Rows[i].Cells["UseNozzleCoordinates_Column"].Value = false;
                        }
                        LoadingDataGrid = false;
                        FillAlgorithmSelectionBox(Tapes_dataGridView, "Type_Column");
                    }
                    else
                    {
                        // read in new format 
                        DisplayText("Loading tapes with nozzles data");
                        LoadTapesFromFile(filename, Tapes_dataGridView);
                    }
                }
            }
            catch (System.Exception excep)
            {
                // Get stack trace for the exception with source file information
                var st = new StackTrace(excep, true);
                // Get the top stack frame
                var frame = st.GetFrame(0);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();
                LoadingDataGrid = false;

                MessageBox.Show(excep.Message);
            }
        }

        private bool AskToCreate(string AlgName, out bool YesToAll, out bool NoToAll)
        {
            YesToAll = false;
            NoToAll = false;
            bool RetVal = false;
            using (var form = new AskToCreate_Form())
            {
                form.Message_TextBox.Text = "Video algorithm \""
                    + AlgName + "\" does not exist.\r\nCreate an empty algorithm with that name?";
                form.StartPosition = FormStartPosition.CenterParent;
                form.ShowDialog();
                YesToAll = form.YesToAll;
                NoToAll = form.NoToAll;
                if (form.YesToAll || form.Yes)
                {
                    RetVal = true;
                }
            }
            return RetVal;
        }


        private void LoadTapesFromFile(string Filename, System.Windows.Forms.DataGridView Grid)
        {
            LoadDataGrid(Filename, Grid, DataTableType.Tapes);
            FillAlgorithmSelectionBox(Grid, "Type_Column");
        }
        private void FillAlgorithmSelectionBox(System.Windows.Forms.DataGridView Grid, string ColumnName)
        {
            // build type combobox and set values
            bool YesToAll = false;
            bool NoToAll = false;
            bool Create;
            bool Exists;
            for (int i = 0; i < Grid.Rows.Count; i++)
            {
                string AlgName = Grid.Rows[i].Cells[ColumnName].Value.ToString();  // value is correct, cell content is not
                                                                                      // Does the algorithm exist?
                if (VideoAlgorithms.AlgorithmExists(AlgName))
                {
                    Create = false;
                    Exists = true;
                }
                else
                {
                    // Algorithm doesn't exist. Do we need to create it?
                    if (YesToAll)
                    {
                        Create = true;
                        Exists = false;
                    }
                    else if (NoToAll)
                    {
                        Create = false;
                        Exists = false;
                    }
                    else
                    {
                        // ask.
                        Create = AskToCreate(AlgName, out YesToAll, out NoToAll);
                        Exists = false;
                    }
                };
                if (Create)
                {
                    AddAlgorithm(AlgName);
                    Exists = true;
                }
                // Build the selection box
                Grid.Rows[i].Cells[ColumnName].Value = null;
                DataGridViewComboBoxCell c = new DataGridViewComboBoxCell();
                BuildAlgorithmsCombox(out c);
                Grid.Rows[i].Cells[ColumnName] = c;
                if (Exists)
                {
                    Grid.Rows[i].Cells[ColumnName].Value = AlgName;
                }
                else
                {
                    // The algorithm didn't exist and didn't get created. Select homing
                    Grid.Rows[i].Cells[ColumnName].Value = VideoAlgorithms.AllAlgorithms[0].Name;
                }
            }
            Update_GridView(Grid);
        }

        private void ReadV1TapesFile(string filename)
        {
            // Logic:
            // Check that file exists.
            // Read the headers.
            // If headers have "WidthColumn", read in to dummy datagridview and convert to new format
            // else read normally
            try
            {
                // read in to old format datagridview and convert to new format
                DisplayText("Loading v1 tapes data");
                LoadDataGrid(filename, Tapes_dataGridView, DataTableType.Tapes);
                // convert to new
                double X;
                double Y;
                double pitch;
                LoadingDataGrid = true;  // to avoid cell value changed events

                for (int i = 0; i < TapesOld_dataGridView.RowCount; i++)
                {
                    if (Tapes_dataGridView.Rows[i].Cells["WidthColumn"].Value != null)
                    {
                        if (Tapes_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString() != "custom")
                        {
                            TapeWidthStringToValues(Tapes_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString(), out X, out Y, out pitch);
                            Tapes_dataGridView.Rows[i].Cells["Pitch_Column"].Value = pitch.ToString(CultureInfo.InvariantCulture);
                            Tapes_dataGridView.Rows[i].Cells["OffsetX_Column"].Value = X.ToString(CultureInfo.InvariantCulture);
                            Tapes_dataGridView.Rows[i].Cells["OffsetY_Column"].Value = Y.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    Tapes_dataGridView.Rows[i].Cells["RotationDirect_Column"].Value = "0.00";
                    Tapes_dataGridView.Rows[i].Cells["CoordinatesForParts_Column"].Value = false;
                    Tapes_dataGridView.Rows[i].Cells["UseNozzleCoordinates_Column"].Value = false;
                }
                LoadingDataGrid = false;
            }
            catch (System.Exception excep)
            {
                // Get stack trace for the exception with source file information
                var st = new StackTrace(excep, true);
                // Get the top stack frame
                var frame = st.GetFrame(0);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();
                LoadingDataGrid = false;

                MessageBox.Show(excep.Message);
            }
        }

        // =================================================================================
        // Scrolling & arranging datagridviews...
        // =================================================================================
        private void DataGrid_Up_button(DataGridView Grid)
        {
            int row = Grid.CurrentCell.RowIndex;
            int col = Grid.CurrentCell.ColumnIndex;
            if (row == 0)
            {
                return;
            }
            if (Grid.SelectedCells.Count != 1)
            {
                return;
            }
            DataGridViewRow temp = Grid.Rows[row];
            Grid.Rows.Remove(Grid.Rows[row]);
            Grid.Rows.Insert(row - 1, temp);
            Grid.ClearSelection();
            Grid.CurrentCell = Grid[col, row - 1];
            HandleGridScrolling(true, Grid);
        }

        private void DataGrid_Down_button(DataGridView Grid)
        {
            int row = Grid.CurrentCell.RowIndex;
            int col = Grid.CurrentCell.ColumnIndex;
            if (row == Grid.RowCount - 1)
            {
                return;
            }
            if (Grid.SelectedCells.Count != 1)
            {
                return;
            }
            DataGridViewRow temp = Grid.Rows[row];
            Grid.Rows.Remove(Grid.Rows[row]);
            Grid.Rows.Insert(row + 1, temp);
            Grid.ClearSelection();
            Grid.CurrentCell = Grid[col, row + 1];
            Grid.Rows[row + 1].Cells[col].Selected = true;
            HandleGridScrolling(false, Grid);
        }

        private void HandleGridScrolling(bool Up, DataGridView Grid)
        {
            // Makes sure the selected row is visible
            int VisibleRows = Grid.DisplayedRowCount(false);
            int FirstVisibleRow = Grid.FirstDisplayedCell.RowIndex;
            int LastVisibleRow = (FirstVisibleRow + VisibleRows) - 1;
            int SelectedRow = Grid.CurrentCell.RowIndex;

            if (VisibleRows >= Grid.RowCount)
            {
                return;
            }

            if (Up)
            {
                if (FirstVisibleRow == 0)
                {
                    return;
                }
                if (SelectedRow < FirstVisibleRow + 2)
                {
                    if (SelectedRow > 2)
                    {
                        Grid.FirstDisplayedScrollingRowIndex = SelectedRow - 2;
                    }
                    else
                    {
                        Grid.FirstDisplayedScrollingRowIndex = 0;
                    }
                }
            }
            else
            {
                if (LastVisibleRow == Grid.RowCount)
                {
                    return;
                }
                if (SelectedRow > LastVisibleRow - 3)
                {
                    if (SelectedRow < Grid.RowCount - 3)
                    {
                        Grid.FirstDisplayedScrollingRowIndex = SelectedRow - VisibleRows + 3;
                    }
                    else
                    {
                        Grid.FirstDisplayedScrollingRowIndex =
                            Grid.RowCount - VisibleRows;
                    }
                }
            }
        }

        // =================================================================================
        // Forcing a DataGridview display update
        // Ugly hack if you ask me, but MS didn't give us any other reliable way...
        public void Update_GridView(DataGridView Grid)
        {
            Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            BindingSource bs = new BindingSource(); // create a BindingSource
            bs.DataSource = Grid.DataSource;  // copy data to bs
            DataTable dat = (DataTable)(bs.DataSource);  // make a datatable from bs
            Grid.DataSource = dat;  // and copy datatable data, forcing redraw
            Grid.RefreshEdit();
            Grid.Refresh();
        }

        public bool MovementIsBusy_m()
        {
            if (JoggingBusy || StartingUp || CNCstarting)
            {
                DisplayText("Other operations are underway, please try again", KnownColor.DarkGreen, true);
                return true;
            }
            if (Cnc.ErrorState || !Cnc.Connected)
            {
                DisplayText("*** Control board not connected or in error state", KnownColor.DarkRed, true);
                return true;
            }
            return false;
        }

        #endregion General

        // =================================================================================
        // Jogging
        // =================================================================================
        #region Jogging

        // see https://github.com/synthetos/TinyG/wiki/TinyG-Feedhold-and-Resume

        // =================================================================================
        private void MouseWheel_event(object sender, MouseEventArgs e)
        {
            if (!MouseScroll_checkBox.Checked)
            {
                return;
            }
            if (MovementIsBusy_m())
            {
                return;
            }

            double Mag = 0.0;
            if (System.Windows.Forms.Control.ModifierKeys == (Keys.Alt | Keys.Shift))
            {
                Mag = 90.0;
            }
            else if (System.Windows.Forms.Control.ModifierKeys == Keys.Alt)
            {
                Mag = 10.0;
            }
            else if (System.Windows.Forms.Control.ModifierKeys == (Keys.Alt | Keys.Control))
            {
                Mag = 4.0;
            }
            else if (System.Windows.Forms.Control.ModifierKeys == Keys.Shift)
            {
                Mag = 1.0;
            }
            else if (System.Windows.Forms.Control.ModifierKeys == Keys.Control)
            {
                Mag = 0.01;
            }
            else
            {
                Mag = 0.1;
            }

            if (e.Delta < 0)
            {
                Mag = -Mag;
            }
            else if (e.Delta > 0)
            {
                // Mag = Mag;
            }
            else
            {
                return;
            }

            JoggingBusy = true;
            DisplayText("Mouse: " + Mag.ToString("0.000", CultureInfo.InvariantCulture), KnownColor.DarkGreen, true);
            CNC_A_m(Cnc.CurrentA + Mag);
            if (DownCamera.Draw_Snapshot)
            {
                DownCamera.RotateSnapshot(Cnc.CurrentA);
                while (DownCamera.rotating)
                {
                    Thread.Sleep(10);
                }
            }
            JoggingBusy = false;
        }


        public bool JoggingBusy { get; set; } = false;
        List<Keys> NumpadKeys = new List<Keys>()
        {
            Keys.NumPad1,   // down + left
	        Keys.NumPad2,   // down
	        Keys.NumPad3,   // down + right
	        Keys.NumPad4,   // left
            Keys.NumPad6,   // right
	        Keys.NumPad7,   // up + left
	        Keys.NumPad8,   // up
 	        Keys.NumPad9,   // up + right
            Keys.Add,
            Keys.Subtract,
            Keys.Divide,
            Keys.Multiply,
        };

        List<Keys> JoggingFKeys = new List<Keys>()
        {
            Keys.F5,
            Keys.F6,
            Keys.F7,
            Keys.F8,
            Keys.F9,
            Keys.F10,
            Keys.F11,
            Keys.F12
        };

        public void My_KeyUp(object sender, KeyEventArgs e)
        {
            if (NumpadKeys.Contains(e.KeyCode))
            {
                // ignore when user is inputting a number
                if ((ActiveControl is TextBox) || (ActiveControl is NumericUpDown) || (ActiveControl is MaskedTextBox))
                {
                    return;
                }
                JoggingBusy = false;
                if (!NumPadJog_checkBox.Checked)
                {
                    return;
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                Cnc.CancelJog();
            }
        }

        static bool EnterKeyHit;

        public void My_KeyDown(object sender, KeyEventArgs e)
        {
        // Enter key finishes assisted placement operation
            if (e.KeyCode == Keys.Enter)
            {
                EnterKeyHit = true;
                return;
            }

        // esc aborts placment (in some situations this is much faster then using the mouse)
            if (e.KeyCode == Keys.Escape)
            {
                AbortPlacement = true;
                AbortPlacementShown = false;
            }

        // F4 shows/hides demo buttons
            if ((e.KeyCode == Keys.F4) &&
                    !((e.Alt) || (e.Control) || (e.Shift))
                )
            {
                Demo_button.Visible = !Demo_button.Visible;
                StopDemo_button.Visible = !StopDemo_button.Visible;
                return;
            }

        // Alt+F4 closes (windows standard)
            if ((e.KeyCode == Keys.F4) && (e.Alt))
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Close program; are you sure?",
                    "Close program?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    Application.Exit();
                }
                e.Handled = true;
                return;
            }

        // F1 opens relevant doc page on browser
            if (e.KeyCode == Keys.F1)
            {
                if (LastTag != null)
                {
                    if (LastTag!="")
                    {
                        System.Diagnostics.Process.Start(LastTag);
                    }

                }
                return;
            }

        // F2 shows/hides nozzle tip info text on Nozzles tab
            if ((e.KeyCode == Keys.F2) && (tabControlPages.SelectedTab.Name == "Nozzles_tabPage"))
            {
                NozzeTip_textBox.Visible = !NozzeTip_textBox.Visible;
            }

        // return, until we need to do jogging
            if (!(NumpadKeys.Contains(e.KeyCode) || JoggingFKeys.Contains(e.KeyCode)))
            {
                return;
            }

        // Handle jogging:
        // No jogging with nozzle down, unil specifically enabled
            if ( ((Cnc.CurrentZ > 3) && (ZguardIsOn()) )
                &&
                !((e.KeyCode == Keys.F11) || (e.KeyCode == Keys.F12)))  // F11&F12: It is ok to move z manually despite of Zguard
            {
                DisplayText("Nozzle is down", KnownColor.DarkRed, true);
                return;
            }

            if (MovementIsBusy_m())
            {
                return;
            }

            if (JoggingFKeys.Contains(e.KeyCode))
            {
                JogFkeys(sender, e);
                return;
            }

        // Numpad keys are numbers on some controls
            if ((ActiveControl is TextBox) || (ActiveControl is NumericUpDown) || (ActiveControl is MaskedTextBox))
            {
                return;
            }

        // Only numpad jogging left
            if (!NumPadJog_checkBox.Checked)
            {
                return;
            }

            JoggingBusy = true;     // will be reset by keyup event
            e.Handled = true;

            DisplayText("Jog: " + e.KeyCode.ToString(), KnownColor.DarkGreen, true);
            string Speedstr;

            if (System.Windows.Forms.Control.ModifierKeys == Keys.Alt)
            {
                Speedstr = AltJogSpeed_numericUpDown.Value.ToString(CultureInfo.InvariantCulture);
            }
            else if (System.Windows.Forms.Control.ModifierKeys == Keys.Control)
            {
                Speedstr = CtlrJogSpeed_numericUpDown.Value.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                Speedstr = NormalJogSpeed_numericUpDown.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (e.KeyCode == Keys.NumPad1)
            {
                Cnc.Jog(Speedstr, "0", "0", "", "");
            }
            else if (e.KeyCode == Keys.NumPad2)
            {
                Cnc.Jog(Speedstr, "", "0", "", "");
            }
            else if (e.KeyCode == Keys.NumPad3)
            {
                Cnc.Jog(Speedstr, Setting.General_MachineSizeX.ToString(CultureInfo.InvariantCulture), "0", "", "");
            }
            else if (e.KeyCode == Keys.NumPad4)
            {
                Cnc.Jog(Speedstr, "0", "", "", "");
            }
            else if (e.KeyCode == Keys.NumPad6)
            {
                Cnc.Jog(Speedstr, Setting.General_MachineSizeX.ToString(CultureInfo.InvariantCulture), "", "", "");
            }
            else if (e.KeyCode == Keys.NumPad7)
            {
                Cnc.Jog(Speedstr, "0", Setting.General_MachineSizeY.ToString(), "", "");
            }
            else if (e.KeyCode == Keys.NumPad8)
            {
                Cnc.Jog(Speedstr, "", Setting.General_MachineSizeY.ToString(CultureInfo.InvariantCulture), "", "");
            }
            else if (e.KeyCode == Keys.NumPad9)
            {
                Cnc.Jog(Speedstr, Setting.General_MachineSizeX.ToString(CultureInfo.InvariantCulture),
                    Setting.General_MachineSizeY.ToString(CultureInfo.InvariantCulture), "", "");
            }
            //     (e.KeyCode == Keys.Add) || (e.KeyCode == Keys.Subtract) || (e.KeyCode == Keys.Divide) || (e.KeyCode == Keys.Multiply))
            else if (e.KeyCode == Keys.Add)
            {
                double Ztarget;
                if (!double.TryParse(Setting.General_Z0toPCB.ToString(CultureInfo.InvariantCulture).Replace(',', '.'), out Ztarget))
                {
                    DisplayText("Z to PCB internal value error!");
                    return;
                }
                double Zadd;
                if (!double.TryParse(Setting.General_BelowPCB_Allowance.ToString(CultureInfo.InvariantCulture).Replace(',', '.'), out Zadd))
                {
                    DisplayText("Below PCB allowance internal value error!");
                    return;
                }
                Ztarget += Zadd;
                Cnc.Jog(Speedstr, "", "", "Z" + Ztarget.ToString(CultureInfo.InvariantCulture), "");
            }
            else if (e.KeyCode == Keys.Subtract)
            {
                Cnc.Jog(Speedstr, "", "", "0", "");
            }
            else if (e.KeyCode == Keys.Divide)
            {
                Cnc.Jog(Speedstr, "", "", "", "0");
            }
            else if (e.KeyCode == Keys.Multiply)
            {
                Cnc.Jog(Speedstr, "", "", "", "100000");  // should be enough
            }
        }

        private void JogFkeys(object sender, KeyEventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            string ModStr="";
            if (e.Alt)
            {
                ModStr = ModStr + "Alt+";
            }
            if (e.Shift)
            {
                ModStr = ModStr + "Shift+";
            }
            if (e.Control)
            {
                ModStr = ModStr + "Control+";
            }
            DisplayText("Jog: " + ModStr + e.KeyCode.ToString(), KnownColor.DarkGreen, true);

            double Mag = 0.0;
            if ((e.Alt) && (e.Shift))
            {
                Mag = 100.0;
            }
            else if ((e.Alt) && (e.Control))
            {
                Mag = 4.0;
            }
            else if (e.Alt)
            {
                Mag = 10.0;
            }
            else if (e.Shift)
            {
                Mag = 1.0;
            }
            else if (e.Control)
            {
                Mag = 0.01;
            }
            else
            {
                Mag = 0.1;
            };

            // move right
            if (e.KeyCode == Keys.F5)
            {
                JoggingBusy = true;
                CNC_XYA_m(Cnc.CurrentX - Mag, Cnc.CurrentY, Cnc.CurrentA);
                e.Handled = true;
                JoggingBusy = false;
                return;
            }

            // move left
            if (e.KeyCode == Keys.F6)
            {
                JoggingBusy = true;
                CNC_XYA_m(Cnc.CurrentX + Mag, Cnc.CurrentY, Cnc.CurrentA);
                e.Handled = true;
                JoggingBusy = false;
                return;
            }

            // move away
            if (e.KeyCode == Keys.F7)
            {
                JoggingBusy = true;
                CNC_XYA_m(Cnc.CurrentX, Cnc.CurrentY + Mag, Cnc.CurrentA);
                e.Handled = true;
                JoggingBusy = false;
                return;
            }

            // move closer
            if (e.KeyCode == Keys.F8)
            {
                JoggingBusy = true;
                CNC_XYA_m(Cnc.CurrentX, Cnc.CurrentY - Mag, Cnc.CurrentA);
                e.Handled = true;
                JoggingBusy = false;
                return;
            };

            // rotate ccw
            if (e.KeyCode == Keys.F9)
            {
                JoggingBusy = true;
                if ((Mag > 99) && (Mag < 101))
                {
                    Mag = 90.0;
                }
                CNC_A_m(Cnc.CurrentA + Mag);
                if (DownCamera.Draw_Snapshot)
                {
                    DownCamera.RotateSnapshot(Cnc.CurrentA);
                    while (DownCamera.rotating)
                    {
                        Thread.Sleep(10);
                    }
                }
                e.Handled = true;
                JoggingBusy = false;
                return;
            }

            // rotate cw
            if (e.KeyCode == Keys.F10)
            {
                JoggingBusy = true;
                if ((Mag > 99) && (Mag < 101))
                {
                    Mag = 90.0;
                }
                CNC_A_m(Cnc.CurrentA - Mag);
                if (DownCamera.Draw_Snapshot)
                {
                    DownCamera.RotateSnapshot(Cnc.CurrentA);
                    while (DownCamera.rotating)
                    {
                        Thread.Sleep(10);
                    }
                }
                e.Handled = true;
                JoggingBusy = false;
                return;
            }

            // move up
            if ((e.KeyCode == Keys.F11) && (Mag < 50))
            {
                JoggingBusy = true;
                CNC_Z_m(Cnc.CurrentZ - Mag);
                e.Handled = true;
                JoggingBusy = false;
                return;
            }

            // move down
            if ((e.KeyCode == Keys.F12) && (Mag < 50))
            {
                JoggingBusy = true;
                CNC_Z_m(Cnc.CurrentZ + Mag);
                JoggingBusy = false;
                e.Handled = true;
                return;
            }

        }

        // =================================================================================
        // Picturebox mouse functions

        private void ImagepositionTo_mms(out double Xmm, out double Ymm, int MouseX, int MouseY, PictureBox Box)
        {
            // Input is mouse position inside picture box.
            // Output is mouse position in mm's from image center (machine position)
            Xmm = 0.0;
            Ymm = 0.0;
            int Xpixels = MouseX - Box.Size.Width / 2;  // X= diff from centern in pixels
            int Ypixels = MouseY - Box.Size.Height / 2;

            Camera cam = DownCamera;
            double pol = 1.0;

            if (DownCamera.Active)
            {
                cam = DownCamera;
            }
            else if (UpCamera.Active)
            {
                pol = -1.0;
                cam = UpCamera;
            }
            else
            {
                DisplayText("No camera running");
                return;
            };

            Xmm = Xpixels * cam.XmmPerScreenPixel() * pol;
            Ymm = Ypixels * cam.YmmPerScreenPixel() * pol;

            DisplayText("BoxTo_mms: MouseX: " + MouseX.ToString(CultureInfo.InvariantCulture)
                + ", X: " + Xpixels.ToString(CultureInfo.InvariantCulture) + ", Xmm: " + Xmm.ToString(CultureInfo.InvariantCulture));
            DisplayText("BoxTo_mms: MouseY: " + MouseY.ToString(CultureInfo.InvariantCulture)
                + ", Y: " + Ypixels.ToString(CultureInfo.InvariantCulture) + ", Ymm: " + Ymm.ToString(CultureInfo.InvariantCulture));
        }


        bool PictureBox_MouseDragged = false;

        private void Cam_pictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (MouseButtons == MouseButtons.Left)
            {
                DisplayText("X: " + e.X.ToString(CultureInfo.InvariantCulture) + ", Y: " + e.Y.ToString(CultureInfo.InvariantCulture));
            }

        }

        private void Cam_pictureBox_MouseClick(object sender, MouseEventArgs e)
        {
            if (System.Windows.Forms.Control.ModifierKeys == Keys.Alt)
            {
                PickColor(e.X, e.Y);
                return;
            }

            if (PictureBox_MouseDragged)
            {
                PictureBox_MouseDragged = false;
                return;
            }

            // if (!CheckPositionConfidence()) return;

            double Xmm, Ymm;
            if (System.Windows.Forms.Control.ModifierKeys == Keys.Control)
            {
                // Cntrl-click
                double X = Convert.ToDouble(e.X) / Convert.ToDouble(Cam_pictureBox.Size.Width);
                X = X * Setting.General_MachineSizeX;
                double Y = Convert.ToDouble(Cam_pictureBox.Size.Height - e.Y) / Convert.ToDouble(Cam_pictureBox.Size.Height);
                Y = Y * Setting.General_MachineSizeY;
                CNC_XYA_m(X, Y, Cnc.CurrentA);
            }

            else
            {
                ImagepositionTo_mms(out Xmm, out Ymm, e.X, e.Y, Cam_pictureBox);
                CNC_XYA_m(Cnc.CurrentX + Xmm, Cnc.CurrentY - Ymm, Cnc.CurrentA);
            }
        }

        // =================================================================================
        private void GoX_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            double X;
            double Y = Cnc.CurrentY;
            double A = Cnc.CurrentA;

            if (!double.TryParse(GotoX_textBox.Text.Replace(',', '.'), out X))
            {
                return;
            }
            if (Relative_Button.Checked)
            {
                X += Cnc.CurrentX;
            }
            CNC_XYA_m(X, Y, A);
        }

        private void GoY_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            double X = Cnc.CurrentX;
            double Y;
            double A = Cnc.CurrentA;
            if (!double.TryParse(GotoY_textBox.Text.Replace(',', '.'), out Y))
            {
                return;
            }
            if (Relative_Button.Checked)
            {
                Y += Cnc.CurrentY;
            }
            CNC_XYA_m(X, Y, A);
        }

        private void GoZ_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            double Z;
            if (!double.TryParse(GotoZ_textBox.Text.Replace(',', '.'), out Z))
            {
                return;
            }
            if (Relative_Button.Checked)
            {
                Z += Cnc.CurrentZ;
            }
            CNC_Z_m(Z);
        }

        private void GoA_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            double X = Cnc.CurrentX;
            double Y = Cnc.CurrentY;
            double A;
            if (!double.TryParse(GotoA_textBox.Text.Replace(',', '.'), out A))
            {
                return;
            }
            if (Relative_Button.Checked)
            {
                A += Cnc.CurrentA;
            }
            CNC_A_m(A);
        }

        private void Goto_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (MovementIsBusy_m())
            {
                return;
            }

            double X;  // target coordinates
            double Y;
            double Z;
            double A;
            if (!double.TryParse(GotoX_textBox.Text.Replace(',', '.'), out X))
            {
                return;
            }
            if (!double.TryParse(GotoY_textBox.Text.Replace(',', '.'), out Y))
            {
                return;
            }
            if (!double.TryParse(GotoZ_textBox.Text.Replace(',', '.'), out Z))
            {
                return;
            }
            if (!double.TryParse(GotoA_textBox.Text.Replace(',', '.'), out A))
            {
                return;
            }

            if (Relative_Button.Checked)
            {
                X += Cnc.CurrentX;
                Y += Cnc.CurrentY;
                Z += Cnc.CurrentZ;
                A += Cnc.CurrentA;
            }
            if (Math.Abs(Z) < 0.01)  // allow raising Z and move at one go
            {
                if (!CNC_Z_m(Z))
                {
                    return;
                }
            };
            // move X, Y, A if needed
            double tol = 0.001;
            if (!((Math.Abs(X - Cnc.CurrentX) < tol) && (Math.Abs(Y - Cnc.CurrentY) < tol) && (Math.Abs(A - Cnc.CurrentA) < tol)))
            {
                // Allow raise Z, goto and low Z:
                if (!(Math.Abs(Z) < tol))
                {
                    if (!CNC_Z_m(0))
                    {
                        return;
                    }
                }
                if (!CNC_XYA_m(X, Y, A))
                {
                    return;
                }
                if (!(Math.Abs(Z - Cnc.CurrentZ) < tol))
                {
                    if (!CNC_Z_m(Z))
                    {
                        return;
                    }
                };
            }
            // move Z if needed
            if (!(Math.Abs(Z - Cnc.CurrentZ) < tol))
            {
                if (!CNC_Z_m(Z))
                {
                    return;
                }
            }
        }

        private void LoadCurrentPosition_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }

            GotoX_textBox.Text = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            GotoY_textBox.Text = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            GotoZ_textBox.Text = Cnc.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture);
            GotoA_textBox.Text = Cnc.CurrentA.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private void SetCurrentPosition_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }

            double tst;
            string Xstr = "";
            string Ystr = "";
            string Zstr = "";
            string Astr = "";
            if (double.TryParse(GotoX_textBox.Text.Replace(',', '.'), out tst))
            {
                Xstr = tst.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                DisplayText("X value error", KnownColor.Red, true);
                return;
            }

            if (double.TryParse(GotoY_textBox.Text.Replace(',', '.'), out tst))
            {
                Ystr = tst.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                DisplayText("Y value error", KnownColor.Red, true);
                return;
            }

            if (double.TryParse(GotoZ_textBox.Text.Replace(',', '.'), out tst))
            {
                Zstr = tst.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                DisplayText("Z value error", KnownColor.Red, true);
                return;
            }

            if (double.TryParse(GotoA_textBox.Text.Replace(',', '.'), out tst))
            {
                Astr = tst.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                DisplayText("A value error", KnownColor.Red, true);
                return;
            }
            Cnc.SetPosition(X: Xstr, Y: Ystr, Z: Zstr, A: Astr);
            Thread.Sleep(50);
        }

        #endregion Jogging

        // =================================================================================
        // CNC interface functions
        // =================================================================================
        #region CNC interface functions

        // =================================================================================
        // moving

        // XY moves are always done as XYA. When less axis are needed, 
        // we use Cnc.Current# replacing axis we don't want to move

        public bool CNC_XYA_m(double X, double Y, double A)
        {
            DisplayText("CNC_XYA_m, x: " + X.ToString("0.000", CultureInfo.InvariantCulture)
                + ", y: " + Y.ToString("0.000", CultureInfo.InvariantCulture) + ", a: " + A.ToString("0.000", CultureInfo.InvariantCulture));
            if (Cnc.ErrorState)
            {
                DisplayText("### Cnc in error state, no move", KnownColor.DarkRed, true);
                return false;
            }
            if (CNC_NozzleIsDown_m())
            {
                return false;
            }
            if (AbortPlacement)
            {
                StopOperation();
                return false;
            }

            if (!CNC_MoveIsSafeX_m(X))
            {
                return false;
            }

            if (!CNC_MoveIsSafeY_m(Y))
            {
                return false;
            }

            if (!Cnc.Connected)
            {
                ShowMessageBox(
                    "CNC_XYA: Cnc not connected",
                    "Cnc not connected",
                    MessageBoxButtons.OK);
                return false;
            }

            A = OptimizeAmove(A);
            if (!Cnc.XYA(X, Y, A))
            {
                ShowMessageBox(
                    "CNC_XYA: Move failed",
                    "Move failed",
                    MessageBoxButtons.OK);
                return false;
            }
            SetOptimizedA();
            return true;
        }


        public bool CNC_Z_m(double Z)
        {
            if (AbortPlacement)
            {
                StopOperation();
                return false;
            }
            if (!Cnc.Connected)
            {
                ShowMessageBox(
                    "CNC_Z: Cnc not connected",
                    "Cnc not connected",
                    MessageBoxButtons.OK);
                return false;
            }

            if (Cnc.ErrorState)
            {
                ShowMessageBox(
                    "CNC_Z: Cnc in error state",
                    "Cncin error state",
                    MessageBoxButtons.OK);
                return false;
            }

            // Switch setup doesn't care about zguard
            if (Setting.NozzleHeightSetupStage != 0)
            {
                return (Cnc.Z(Z));
            }

            if ((ZguardIsOn()) && (Z > (Setting.General_Z0toPCB + Setting.General_BelowPCB_Allowance)))
            {
                DialogResult dialogResult = ShowMessageBox(
                    "The operation seems to take the Nozzle below safe level. Continue?",
                    "Z below table", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    return false;
                };

            }
            return (Cnc.Z(Z));
        }

        public double OptimizeAmove(double A)
        {
            if (Setting.CNC_OptimizeA)
            {
                DisplayText("OptimizeA");
                NormalizeRotation(ref A);

                double MinusDir = Cnc.CurrentA - A;
                double PlusDir = A - Cnc.CurrentA;
                NormalizeRotation(ref PlusDir);
                NormalizeRotation(ref MinusDir);
                if (PlusDir < MinusDir)
                {
                    return (Cnc.CurrentA + PlusDir);
                }
                else
                {
                    return (Cnc.CurrentA - MinusDir);
                }
            }
            else
            {
                return A;
            }
        }

        public void SetOptimizedA()
        {
            if (Setting.CNC_OptimizeA)
            {
                double tmpA = Cnc.CurrentA;
                NormalizeRotation(ref tmpA);
                if (Math.Abs(tmpA-360.0)<0.0009)
                {
                    tmpA = 0.0;
                }
                Cnc.CurrentA = tmpA;
                Cnc.SetPosition(X: "", Y: "", Z: "", A: Cnc.CurrentA.ToString("0.000", CultureInfo.InvariantCulture));
            }
        }

        public bool CNC_A_m(double A)
        {
            DisplayText("CNC_A_m, a: " + A.ToString(CultureInfo.InvariantCulture));
            if (Cnc.ErrorState)
            {
                DisplayText("### Cnc in error state, ignored", KnownColor.DarkRed, true);
                return false;
            }
            if (Cnc.ErrorState)
            {
                DisplayText("*** Cnc.CNC_A_m(), board in error state.", KnownColor.DarkRed, true);
                return false;
            }

            if (!Cnc.Connected)
            {
                DisplayText("*** Cnc.CNC_A_m(), board not connected.", KnownColor.DarkRed, true);
                return false;
            }
            A = OptimizeAmove(A);
            if (!Cnc.A(A)) return false;
            SetOptimizedA();
            return true;
        }

        private void NormalizeRotation(ref double rot)
        {
            while (rot > 360.0)
            {
                rot -= 360.0;
            }
            while (rot < 0.0)
            {
                rot += 360.0;
            }
        }
        // =================================================================================
        // move guards

        private bool CNC_MoveIsSafeX_m(double X)
        {
            if ((X < -Math.Abs(Setting.General_NegativeX)) || (X > Setting.General_MachineSizeX))
            {
                ShowMessageBox(
                    "Attempt to Move outside machine limits (X " + X.ToString("0.000", CultureInfo.InvariantCulture) + ")",
                    "Limits error",
                    MessageBoxButtons.OK);
                return false;
            }
            return true;
        }

        private bool CNC_MoveIsSafeY_m(double Y)
        {
            if ((Y < -Math.Abs(Setting.General_NegativeY)) || (Y > Setting.General_MachineSizeY))
            {
                ShowMessageBox(
                    "Attempt to Move outside machine limits (Y " + Y.ToString("0.000", CultureInfo.InvariantCulture) + ")",
                    "Limits corssed",
                    MessageBoxButtons.OK);
                return false;
            }
            return true;
        }

        private bool _Zguard = true;
        private bool ZguardIsOn()
        {
            return _Zguard;
        }

        private void ZGuardOn()
        {
            _Zguard = true;
            DisplayText("Z guard on");
        }

        private void ZGuardOff()
        {
            _Zguard = false;
            DisplayText("Z guard off");
        }

        private bool CNC_NozzleIsDown_m()
        {
            if ((Cnc.CurrentZ > 5) && ZguardIsOn())
            {
                DisplayText("Nozzle down error.");
                ShowMessageBox(
                   "Attempt to Move while Nozzle is down.",
                   "Danger to Nozzle",
                   MessageBoxButtons.OK);
                return true;
            }
            return false;
        }


         // =================================================================================
        // Homing
        // =================================================================================

        // =======================
        // CheckPositionConfidence() so that on buttons, we can do:
        // if(!CheckPositionConfidence()) return; -- if position is lost and user doesn't want to homing, do nothing;

        private bool CheckPositionConfidence()
        {
            if (PositionConfidence)
            {
                return true;
            }
            return OfferHoming();
        }


        private bool OfferHoming()
        {
            if (Cnc.ErrorState)
            {
                DisplayText("*** OfferHoming(), board in error state.", KnownColor.DarkRed, true);
                return false;
            }

            PositionConfidence = false;
            OpticalHome_button.BackColor = Color.Red;
            DialogResult dialogResult = ShowMessageBox(
              "Home machine now?",
                "Home Now?", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                return DoHoming();
            }
            else
            {
                return false;
            }
        }

        private bool DoHoming()
        {
            if (MovementIsBusy_m())
            {
                return false;
            }
            OpticalHome_button.Enabled = false;
            PositionConfidence = false;
            OpticalHome_button.BackColor = Color.Red;
            ValidMeasurement_checkBox.Checked = false;
            MeasureAndSet_button.Enabled = false;
            if (!MechanicalHoming_m())
            {
                OpticalHome_button.BackColor = Color.Red;
                OpticalHome_button.Enabled = true;
                return false;
            }
            if (!OpticalHoming_m())
            {
                OpticalHome_button.BackColor = Color.Red;
                OpticalHome_button.Enabled = true;
                return false;
            }
            if (VigorousHoming_checkBox.Checked)
            {
                // shake the machine
                if (!DoTheShake())
                {
                    OpticalHome_button.Enabled = true;
                    return false;
                }
                // home again
                if (!OpticalHoming_m())
                {
                    OpticalHome_button.BackColor = Color.Red;
                    OpticalHome_button.Enabled = true;
                    return false;
                }
            }
            OpticalHome_button.BackColor = default(Color);
            OpticalHome_button.UseVisualStyleBackColor = true;
            PositionConfidence = true;
            MeasureAndSet_button.Enabled = true;
            OpticalHome_button.Enabled = true;
            return true;
        }

        private bool DoTheShake()
        {
            if (Cnc.ErrorState)
            {
                DisplayText("*** Vigorous homing(), board in error state.", KnownColor.DarkRed, true);
                return false;
            }

            DisplayText("Vigorous homing");
            if ((Setting.General_MachineSizeX < 300) || (Setting.General_MachineSizeY < 300))
            {
                DisplayText("Machine too small for vigorous homing routine (Please give feedback!)");
                return true;
            }
            int[] x = new int[] { 250, 250, 250, 50, 50, 0 };
            int[] y = new int[] { 250, 50, 150, 50, 150, 0 };
            for (int i = 0; i < x.Length; i++)
            {
                if (!CNC_XYA_m(x[i], y[i], Cnc.CurrentA))
                {
                    return false;
                }
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!CNC_XYA_m(x[i], y[i], Cnc.CurrentA))
                {
                    return false;
                }
            }
            return true;
        }


        private bool MechanicalHoming_m()
        {
            if (Cnc.ErrorState)
            {
                DisplayText("*** MechanicalHoming_m(), board in error state.", KnownColor.DarkRed, true);
                return false;
            }

            if (!HomeZ_m())
            {
                return false;
            };
            // DisplayText("move Z");
            if (!HomeY_m())
            {
                return false;
            };
            if (!CNC_Z_m(Setting.General_ShadeGuard_mm))		// make room for shade
            {
                return false;
            };
            if (!HomeX_m())
            {
                return false;
            };
            // DisplayText("move A");
            if (!CNC_A_m(0))
            {
                return false;
            };
            if (Setting.General_ShadeGuard_mm > 0.0)
            {
                ZGuardOff();
                if (!CNC_XYA_m(10, 10, Cnc.CurrentA))
                {
                    ZGuardOn();
                    return false;
                };
                DisplayText("Z back up Z");  // Z back up
                if (!CNC_Z_m(0))
                {
                    ZGuardOn();
                    return false;
                };
                ZGuardOn();
            };
            if (tabControlPages.SelectedTab.Name == "Nozzles_tabPage")
            {
                Cnc.DisableZswitches();
            }
            return true;
        }

        public bool Homing = false;
        private void OpticalHome_button_Click(object sender, EventArgs e)
        {
            Homing = true;
            DoHoming();
            Homing = false;
        }


        private bool OpticalHoming_m()
        {
            if (Cnc.ErrorState)
            {
                DisplayText("*** OpticalHoming_m(), board in error state.", KnownColor.DarkRed, true);
                return false;
            }

            DisplayText("Optical homing");
            VideoAlgorithmsCollection.FullAlgorithmDescription HomeAlg = new VideoAlgorithmsCollection.FullAlgorithmDescription();
            if (!VideoAlgorithms.FindAlgorithm("Homing", out HomeAlg))
            {
                DisplayText("*** Homing algorithm not found - programming error or corrupt data file!", KnownColor.Red, true);
                return false;
            }

            // Homing switches to downcamera. Keep UI updated on setup pages
            if (LastTabPage == "tabPageSetupCameras")
            {
                Do_ConnectDownCamera_button_Click();
            }
            if (LastTabPage == "Algorithms_tabPage")
            {
                DownCam_radioButton.Checked = true;
            }

            DownCamera.BuildMeasurementFunctionsList(HomeAlg.FunctionList);
            DownCamera.MeasurementParameters = HomeAlg.MeasurementParameters;

            double X;
            double Y;
            double A = 0.0;
            bool ok = true;
            do
            {
                ok = true;
                if (!GoToFeatureLocation_m(0.1, out X, out Y, out A))
                {
                    ok = false;
                    string nl = Environment.NewLine;
                    string answer = NonModalMessageBox(
                        "Move to home mark failed." + nl +
                        "Jog machine to position and/or tune the algorithm." + nl +
                        "Click \"Retry\" to try again," + nl +
                        "click \"Cancel\" to continue without success", "Home Mark not found",
                        "Retry", "", "Cancel");
                    if (answer == "Cancel")
                    {
                        return false;
                    }
                    DownCamera.BuildMeasurementFunctionsList(HomeAlg.FunctionList);
                    DownCamera.MeasurementParameters = HomeAlg.MeasurementParameters;
                    Thread.Sleep(100);
                }
            } while (!ok);

            List<double> Xlist = new List<double>();
            List<double> Ylist = new List<double>();
            // Once on top of the homing mark, get median from 7 new measurements.
            // (The measurement might be off by a pixel. This gets the most likely correct result.)
            do
            {
                Xlist.Clear();
                Ylist.Clear();
                int Successes = 0;
                int Tries = 0;
                do
                {
                    Tries++;
                    if (DownCamera.Measure(out X, out Y, out double Ares, false))
                    {
                        Successes++;
                        Xlist.Add(X);
                        Ylist.Add(Y);
                    }
                }
                while ((Successes < 7) && (Tries < 20));
                ok = true;
                if (Tries >= 20)
                {
                    ok = false;
                    string nl = Environment.NewLine;
                    string answer = NonModalMessageBox(
                        "Secondary measurement failed, 20 measurements did not give 7 good results." + nl +
                        "Try tuning the algorithm further." + nl +
                        "Click \"Retry\" to try again," + nl +
                        "Click \"Cancel\" to continue without success", "Measurement failed",
                        "Retry", "", "Cancel");
                    if (answer == "Cancel")
                    {
                        return false;
                    }
                    DownCamera.BuildMeasurementFunctionsList(HomeAlg.FunctionList);
                    DownCamera.MeasurementParameters = HomeAlg.MeasurementParameters;
                    Thread.Sleep(100);
                }
            } while (!ok);

            Xlist.Sort();
            Ylist.Sort();
            X = -Xlist[3];
            Y = -Ylist[3];
            Cnc.SetPosition(X: X.ToString("0.000", CultureInfo.InvariantCulture),
                Y: Y.ToString("0.000", CultureInfo.InvariantCulture), Z: "", A: "");
            Thread.Sleep(50);
            Cnc.CurrentX = X;
            Cnc.CurrentY = Y;
            Update_Xposition();
            Update_Yposition();
            DisplayText("Optical homing OK.");
            if (Setting.General_Autopark)
            {
                CNC_Park();
            }
            return true;
        }



        // =================================================================================
        // Misc CNC functions

        // =================================================================================
        // UI position update 

        #region Position

        public void Update_Xposition()
        {
            if (InvokeRequired) { Invoke(new Action(Update_Xposition)); return; }
            TrueX_label.Text = Cnc.TrueX.ToString("0.000", CultureInfo.InvariantCulture);
            Xposition_textBox.Text = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
        }

        public void Update_Yposition()
        {
            if (InvokeRequired) { Invoke(new Action(Update_Yposition)); return; }
            Yposition_textBox.Text = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
        }

        public void Update_Zposition()
        {
            if (InvokeRequired) { Invoke(new Action(Update_Zposition)); return; }
            Zposition_textBox.Text = Cnc.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture); 
        }

        public void Update_Aposition()
        {
            if (InvokeRequired) { Invoke(new Action(Update_Aposition)); return; }
            Aposition_textBox.Text = Cnc.CurrentA.ToString("0.000", CultureInfo.InvariantCulture);
        }

        #endregion Position


        private void CNC_Park()
        {
            if (Cnc.ErrorState)
            {
                DisplayText("*** CNC_Park(), board in error state.", KnownColor.DarkRed, true);
                return;
            }

            DisplayText("Goto park");
            if (CNC_Z_m(0))
            {
                CNC_XYA_m(Setting.General_ParkX, Setting.General_ParkY, 0.0);
            }
        }

        private void StopOperation()
        {
            if (!AbortPlacementShown)
            {
                AbortPlacementShown = true;
                ShowMessageBox(
                           "Operation aborted",
                           "Operation aborted",
                           MessageBoxButtons.OK);
            }
            AbortPlacement = false;
            if (!Check_zzb())
            {
                return;
            }
            if (Math.Abs(Cnc.CurrentZ) > 0.01)
            {
                CNC_Z_m(0.0);
            }
        }

        // =================================================================================
        // Different types of control hardware and settings
        // =================================================================================

        // =================================================================================
        // Position confidence, motor power timer:
        // we want to keep track about TinyG motor power state. To do that, we run our own timer, MotorPower_timer.
        // The timer ticks every second and runs all the time. In normal operation state, we count the ticks and
        // reset the count every time the machine stops.
        // The TinyG motor power timeout value is retrieved at startup, like other TinyG values.
        // If the machine is homed, and either timer get bigger than motor power timeout (motors are powered off by TinyG),
        // or an error occurs, we've lost position confidence and machine needs to be re-homed.

        private double PowerTimerCount = 0;
        private double TinyGMotorTimeout = 300.0;
        private bool PositionConfidence = false;

        private void Update_mt(string ValStr)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_mt), new[] { ValStr }); return; }

            double val;
            if (double.TryParse(ValStr.Replace(',', '.'), out val))
            {
                TinyGMotorTimeout = val;
                DisplayText("mt value: " + TinyGMotorTimeout.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                DisplayText("Bad mt value: " + ValStr, KnownColor.DarkRed, true);
            }

        }

        // Timer start-stop loses event hook if done from another thread. Therefore,
        // let the timer run all the time, and keep track if we've done the task already
        public bool TimerDone = false;

        public void ResetMotorTimer()
        {
            SetMotorPower_checkBox(true);
            PowerTimerCount = 0;
            TimerDone = false;
        }

        public void SetMotorPower_checkBox(bool val) // to get the set/clear to UI thread
        {
            // Running on the worker thread
            this.MotorPower_checkBox.Invoke((MethodInvoker)delegate
            {
                // Running on the UI thread
                MotorPower_checkBox.Checked = val;
            });
        }

        [DebuggerStepThrough]
        private bool IsTimerDone()
        {
            return TimerDone;
        }

        [DebuggerStepThrough]
        private void MotorPower_timer_Tick(object sender, EventArgs e)
        {
            if (Setting.Controlboard == ControlBoardType.Marlin)
            {
                return;
            }

            if (IsTimerDone())
            {
                return;
            };
            PowerTimerCount = PowerTimerCount + 1.0;
            if ((PowerTimerCount + 0.1) > TinyGMotorTimeout)
            {
                SetMotorPower_checkBox(false);
                TimerDone = true;
                if (PositionConfidence)         // == if timer should run
                {
                    OfferHoming();
                }
            }
        }


        // =================================================================================

        private int _cnc_Timeout = 3000; // timeout for X,Y,Z,A movements; 2x ms. (3000= 6s timeout)
        private int CNC_timeout
        {
            get
            {
                return _cnc_Timeout / 500;
            }
            set
            {
                _cnc_Timeout = value * 500;
            }
        }


        // =====================================================================
        // This routine finds an accurate location of a circle/rectangle/... that downcamera is looking at.
        // Used in homing, locating fiducials, components, tape holes or pockets...
        // At return, the camera is located on top of the feature (within MoveTolerance). 
        // X and Y are set to remaining error (true position: currect + error)
        // Measurement is already set up
        // =====================================================================

        public bool GoToFeatureLocation_m(double MoveTolerance, out double X, out double Y, out double A)
        {
            X = 100;
            Y = 100;
            A = 0;
            if (Cnc.ErrorState)
            {
                DisplayText("*** GoToFeatureLocation_m(), board in error state.", KnownColor.DarkRed, true);
                return false;
            }

            DisplayText("GoToFeatureLocation_m()");
            SelectCamera(DownCamera);
            if (!DownCamera.IsRunning())
            {
                DisplayText("***Camera not running", KnownColor.Red, true);
                return false;
            }
            int count = 0;
            int tries = 0;

            do
            {
                for (tries = 0; tries < 8; tries++)
                {
                    if (DownCamera.Measure(out X, out Y, out A, false))
                    {
                        break;
                    }
                    Thread.Sleep(80); // next frame + vibration damping
                    if (tries >= 7)
                    {
                        return false;
                    }
                }
                DisplayText("Optical positioning, round " + count.ToString(CultureInfo.InvariantCulture)
                    + ", dX= " + X.ToString("0.000", CultureInfo.InvariantCulture) + ", dY= " + Y.ToString("0.000", CultureInfo.InvariantCulture)
                    + ", A= " + X.ToString("0.000", CultureInfo.InvariantCulture) + ", tries= " + tries.ToString(CultureInfo.InvariantCulture));
                // If we are further than move tolerance, go there
                if ((Math.Abs(X) > MoveTolerance) || (Math.Abs(Y) > MoveTolerance))
                {
                    if (!CNC_XYA_m(Cnc.CurrentX + X, Cnc.CurrentY + Y, A))
                    {
                        return false;
                    }
                }
                else
                {
                    break;
                }
                count++;
            }  // repeat this until we didn't need to move
            while (count < 8);
//                && ((Math.Abs(X) > MoveTolerance)
//                || (Math.Abs(Y) > MoveTolerance)));

            if (count >= 7)
            {
                ShowMessageBox(
                    "Optical positioning: Process is unstable, result is unreliable.",
                    "Count exeeded",
                    MessageBoxButtons.OK);
                return false;
            }
            return true;
        }

        #endregion CNC interface functions

        // =================================================================================
        // Up/Down camera setup page functions
        // =================================================================================
        #region Camera setup pages functions

        // =================================================================================
        // Common
        // =================================================================================
        int DownCamFrameMeasuresCount = 0;
        Queue<int> DownCamFrameMeasures = new Queue<int>();

        private void InitDownCamFpsMeasurement()
        {
            DownCamFrameMeasures.Clear();
            DownCamFrameMeasuresCount = 0;
            FPStimer.Enabled = true;
            FPStimer.Start();
            DisplayText("InitDownCamFpsMeasurement()");
        }

        int UpCamFrameMeasuresCount = 0;
        Queue<int> UpCamFrameMeasures = new Queue<int>();

        private void InitUpCamFpsMeasurement()
        {
            UpCamFrameMeasures.Clear();
            UpCamFrameMeasuresCount = 0;
            DisplayText("InitUpCamFpsMeasurement()");
        }

        private string ReportFPS(Camera Cam, ref int MeasuresCount, ref Queue<int> Measures)
        {
            double fps = 0;
            int FrCount;
            if (!Cam.IsRunning())
            {
                return "frame rate: --";
            }
            FrCount = Cam.FrameCount;
            Cam.FrameCount = 0;
            MeasuresCount++;
            if (MeasuresCount == 1)
            {
                return "frame rate: --";
            }
            Measures.Enqueue(FrCount);
            if (MeasuresCount > 5)   // queue is full
            {
                Measures.Dequeue();
            }
            fps = (double)Measures.Sum() / (double)Measures.Count();
            return "frame rate: " + fps.ToString("00.0", CultureInfo.InvariantCulture) + " fps";
        }


        [DebuggerStepThrough]
        private void FPStimer_Tick(object sender, EventArgs e)
        {
            DownCameraFps_label.Text = ReportFPS(DownCamera, ref DownCamFrameMeasuresCount, ref DownCamFrameMeasures);
            UpCameraFps_label.Text = ReportFPS(UpCamera, ref UpCamFrameMeasuresCount, ref UpCamFrameMeasures);
        }

        private void KeepActive_checkBox_Click(object sender, EventArgs e)
        {
            // We handle checked state ourselves to avoid automatic call to StartCameras at startup
            // (Changing Checked activates CheckedChanged event
            if (KeepActive_checkBox.Checked)
            {
                // KeepActive_checkBox.Checked = true;
                RobustFast_checkBox.Enabled = false;
                StartCameras();
            }
            else
            {
                // KeepActive_checkBox.Checked = false;
                RobustFast_checkBox.Enabled = true;
            }
            Setting.Cameras_KeepActive = KeepActive_checkBox.Checked;
            UpdateDownCameraStatusLabel();
            UpdateUpCameraStatusLabel();
        }


        private void StartCameras()
        {
            // Called at startup. 
            DownCamera.Active = false;
            UpCamera.Active = false;
            DownCamera.Close();
            UpCamera.Close();
            SetDownCameraDefaults();
            SetUpCameraDefaults();
            if (KeepActive_checkBox.Checked)
            {
                StartUpCamera_m();
                UpCamera.Active = false;
                StartDownCamera_m();
            }
            SelectCamera(DownCamera);

        }

 // ==================================================
        private void UpdateDownCameraStatusLabel()
        {
            if (!DownCamera.IsRunning())
            {
                DownCameraStatus_label.Text = "Off";
                DownCamUsedResolution_label.Text = "resolution: --";
                DownCameraFps_label.Text = "frame rate: --";
                return;
            }
            if (DownCamera.Active)
            {
                DownCameraStatus_label.Text = "Active";
                DownCamUsedResolution_label.Text = "resolution: " + DownCamera.CameraResolution.X.ToString() + 
                    " x " + DownCamera.CameraResolution.Y.ToString();
            }
            else
            {
                DownCameraStatus_label.Text = "On, not active";
                DownCamUsedResolution_label.Text = "resolution: " + DownCamera.CameraResolution.X.ToString() + 
                    " x " + DownCamera.CameraResolution.Y.ToString();
            }

        }

        private void UpdateUpCameraStatusLabel()
        {
            if (!UpCamera.IsRunning())
            {
                UpCameraStatus_label.Text = "Off";
                UpCamUsedResolution_label.Text = "resolution: --";
                UpCameraFps_label.Text = "frame rate: --";
                return;
            }
            if (UpCamera.Active)
            {
                UpCameraStatus_label.Text = "Active";
                UpCamUsedResolution_label.Text = "resolution: " + UpCamera.CameraResolution.X.ToString() + 
                    " x " + UpCamera.CameraResolution.Y.ToString();
            }
            else
            {
                UpCameraStatus_label.Text = "On, not active";
                UpCamUsedResolution_label.Text = "resolution: " + UpCamera.CameraResolution.X.ToString() +
                    " x " + UpCamera.CameraResolution.Y.ToString();
            }
        }



        private void UpCamStop_button_Click(object sender, EventArgs e)
        {
            UpCamera.Close();
            UpCamera.Active = false;
            UpdateUpCameraStatusLabel();
        }

        private void UpCamStart_button_Click(object sender, EventArgs e)
        {
            StartUpCamera_m();
        }
        private void DownCamStop_button_Click(object sender, EventArgs e)
        {
            DownCamera.Close();
            DownCamera.Active = false;
            UpdateDownCameraStatusLabel();
        }

        private void DownCamStart_button_Click(object sender, EventArgs e)
        {
            StartDownCamera_m();
        }


        private void RobustFast_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Cameras_RobustSwitch = RobustFast_checkBox.Checked;
        }


        private void SelectCamera(Camera cam)
        {
            if (cam.IsRunning() && cam.Active)
            {
                DisplayText(cam.Name + " already on");
                return;
            }
            if (cam.MonikerString == "-no camera-")
            {
                DisplayText("Selecting, no camera");
                return;
            }
            if (KeepActive_checkBox.Checked)
            {
                DownCamera.Active = false;
                UpCamera.Active = false;
                cam.Active = true;
                if (cam == DownCamera)
                {
                    DisplayText("DownCamera activated");
                }
                else
                {
                    DisplayText("UpCamera activated");
                }
                return;
            };
            /*
            if (cam.IsRunning())
            {
                if (cam == DownCamera)
                {
                    DisplayText("DownCamera already running.");
                }
                else
                {
                    DisplayText("UpCamera already running.");
                }
                return;
            };
            */
            if (Setting.Cameras_RobustSwitch)
            {
                if (UpCamera.IsRunning())
                {
                    UpCamera.Close();
                };
                if (DownCamera.IsRunning())
                {
                    DownCamera.Close();
                };
            }
            if (cam == DownCamera)
            {
                if (UpCamera.IsRunning())
                {
                    UpCamera.Close();
                };
                StartDownCamera_m();
            }
            else
            {
                if (DownCamera.IsRunning())
                {
                    DownCamera.Close();
                };
                StartUpCamera_m();
            }
        }


        // =================================================================================

        private bool StartDownCamera_m()
        {
            UpCamera.Active = false;
            if (DownCamera.IsRunning())
            {
                DisplayText("DownCamera already running");
                DownCamera.Active = true;
                UpdateDownCameraStatusLabel();
                return true;
            };

            DownCamera.Active = false;
            if (string.IsNullOrEmpty(Setting.DowncamMoniker))
            {
                // Very first runs, no attempt to connect cameras yet. This is ok.
                UpdateDownCameraStatusLabel();
                return true;
            };
            // Check that the device exists
            List<string> monikers = DownCamera.GetMonikerStrings();
            if (!monikers.Contains(Setting.DowncamMoniker))
            {
                DisplayText("Downcamera moniker not found. Moniker: " + Setting.DowncamMoniker);
                UpdateDownCameraStatusLabel();
                return false;
            }

            if (Setting.UpcamMoniker == Setting.DowncamMoniker)
            {
                ShowMessageBox(
                    "Up camera and Down camera point to same physical device.",
                    "Camera selection issue",
                    MessageBoxButtons.OK
                );
                UpdateDownCameraStatusLabel();
                return false;
            }

            if (!DownCamera.Start("DownCamera", Setting.DowncamMoniker, Setting.DownCam_UseMaxResolution))
            {
                ShowMessageBox(
                    "Problem Starting down camera.",
                    "Down Camera problem",
                    MessageBoxButtons.OK
                );
                DownCamera.Active = false;
                UpdateDownCameraStatusLabel();
                return false;
            };
            DownCamera.Active = true;
            UpdateDownCameraStatusLabel();
            InitDownCamFpsMeasurement();
            return true;
        }

        // ====


        private bool StartUpCamera_m()
        {

            DownCamera.Active = false;
            if (UpCamera.IsRunning())
            {
                DisplayText("UpCamera already running");
                UpCamera.Active = true;
                UpdateUpCameraStatusLabel();
                return true;
            };

            UpCamera.Active = false;
            if (string.IsNullOrEmpty(Setting.UpcamMoniker))
            {
                // Very first runs, no attempt to connect cameras yet. This is ok.
                UpdateUpCameraStatusLabel();
                return true;
            };
            // Check that the device exists
            List<string> monikers = UpCamera.GetMonikerStrings();
            if (!monikers.Contains(Setting.UpcamMoniker))
            {
                DisplayText("Upcamera moniker not found. Moniker: " + Setting.UpcamMoniker);
                UpdateUpCameraStatusLabel();
                return false;
            }

            if (Setting.UpcamMoniker == Setting.DowncamMoniker)
            {
                ShowMessageBox(
                    "Up camera and Down camera point to same physical device.",
                    "Camera selection issue",
                    MessageBoxButtons.OK
                );
                UpdateUpCameraStatusLabel();
                return false;
            }

            if (!UpCamera.Start("UpCamera", Setting.UpcamMoniker, Setting.UpCam_UseMaxResolution))
            {
                ShowMessageBox(
                    "Problem Starting up camera.",
                    "Up camera problem",
                    MessageBoxButtons.OK
                );
                UpdateUpCameraStatusLabel();
                return false;
            };
            UpCamera.Active = true;
            UpdateUpCameraStatusLabel();
            InitUpCamFpsMeasurement();
            return true;
        }

        // =================================================================================


        // =================================================================================


        private void SetDownCameraDefaults()
        {
            DownCamera.DesiredResolutionX = Setting.DownCam_DesiredX;
            DownCamera.DesiredResolutionY = Setting.DownCam_DesiredY;

            DownCamera.ImageBox = Cam_pictureBox;
            System.Drawing.Point pt = new System.Drawing.Point();
            pt.X = Cam_pictureBox.Width;
            pt.Y = Cam_pictureBox.Height;
            DownCamera.DisplayResolution = pt;

            DownCamera.BoxSizeX = 200;
            DownCamera.BoxSizeY = 200;
            DownCamera.BoxRotationDeg = 0;
            DownCamera.Mirror = Setting.Downcam_Mirror;
            DownCamera.ClearDisplayFunctionsList();
            DownCamera.SnapshotColor = Setting.DownCam_SnapshotColor;
            // Draws
            /*
            DownCamera.DrawCross = true;
            DownCamera.DrawDashedCross = false;
            DownCamera.DrawGrid = false;
            DownCamera.Draw_Snapshot = false;
            */
            // Finds:
            DownCamera.FindCircles = false;
            DownCamera.FindRectangles = false;
            DownCamera.FindComponentByOutlines = false;
            DownCamera.FindComponentByPads = false;
            DownCamera.TestAlgorithm = false;
            DownCamera.DrawBox = false;
            DownCamera.DrawArrow = false;

            DownCamera.SideMarksX = Setting.General_MachineSizeX / 100;
            DownCamera.SideMarksY = Setting.General_MachineSizeY / 100;
            DownCamera.XmmPerPixel = Setting.DownCam_XmmPerPixel;
            DownCamera.YmmPerPixel = Setting.DownCam_YmmPerPixel;
            if (Setting.DownCam_MeasurementDelay>20)
            {
                // leftover from older revision, where the values was in milliseconds)
                Setting.DownCam_MeasurementDelay = 10;
            }
            if (Setting.DownCam_MeasurementDelay >= MaxDelay)
            {
                // Fixing a bug that might have resulted to a bad settings file
                Setting.DownCam_MeasurementDelay = MaxDelay - 1;
            }
            DownCamera.MeasurementDelay = Setting.DownCam_MeasurementDelay;
        }

        // ====
        private void SetUpCameraDefaults()
        {
            UpCamera.DesiredResolutionX = Setting.UpCam_DesiredX;
            UpCamera.DesiredResolutionY = Setting.UpCam_DesiredY;

            UpCamera.ImageBox = Cam_pictureBox;
            System.Drawing.Point pt = new System.Drawing.Point();
            pt.X = Cam_pictureBox.Width;
            pt.Y = Cam_pictureBox.Height;
            UpCamera.DisplayResolution = pt;

            UpCamera.BoxSizeX = 200;
            UpCamera.BoxSizeY = 200;
            UpCamera.BoxRotationDeg = 0;
            UpCamera.Mirror = Setting.Upcam_Mirror;

            UpCamera.ClearDisplayFunctionsList();
            UpCamera.SnapshotColor = Setting.UpCam_SnapshotColor;
            // Draws
            UpCamera.DrawCross = true;
            UpCamera.DrawGrid = false;
            UpCamera.DrawDashedCross = false;
            UpCamera.Draw_Snapshot = false;
            // Finds:
            UpCamera.FindCircles = false;
            UpCamera.FindRectangles = false;
            DownCamera.FindComponentByOutlines = false;
            DownCamera.FindComponentByPads = false;
            UpCamera.TestAlgorithm = false;
            UpCamera.DrawBox = false;
            UpCamera.DrawArrow = false;

            UpCamera.SideMarksX = Setting.General_MachineSizeX / 100;
            UpCamera.SideMarksY = Setting.General_MachineSizeY / 100;
            UpCamera.XmmPerPixel = Setting.UpCam_XmmPerPixel;
            UpCamera.YmmPerPixel = Setting.UpCam_YmmPerPixel;
            if (Setting.UpCam_MeasurementDelay > 20)
            {
                // leftover from older revision, where the values was in milliseconds)
                Setting.UpCam_MeasurementDelay = 0;
            }

            if (Setting.UpCam_MeasurementDelay >= MaxDelay)
            {
                // Fixing a bug that might have resulted to a bad settings file
                Setting.UpCam_MeasurementDelay = MaxDelay - 1;
            }
            UpCamera.MeasurementDelay = Setting.UpCam_MeasurementDelay;
        }

        // =================================================================================
        private void tabPageSetupCameras_Begin()
        {
            DisplayText("Setup Cameras tab begin");
            SetDownCameraDefaults();
            DownCameraDesiredX_textBox.Text = Setting.DownCam_DesiredX.ToString(CultureInfo.InvariantCulture);
            DownCameraDesiredY_textBox.Text = Setting.DownCam_DesiredY.ToString(CultureInfo.InvariantCulture);
            DownCamDrawSidemarks_checkBox.Checked = Setting.DownCam_DrawSidemarks;
            DownCamDrawCross_checkBox.Checked = DownCamera.DrawCross;
            DownCamDrawBox_checkBox.Checked = DownCamera.DrawBox;

            SetUpCameraDefaults();
            UpCameraDesiredX_textBox.Text = Setting.UpCam_DesiredX.ToString(CultureInfo.InvariantCulture);
            UpCameraDesiredY_textBox.Text = Setting.UpCam_DesiredY.ToString(CultureInfo.InvariantCulture);
            UpCamDrawCross_checkBox.Checked = UpCamera.DrawCross;
            UpCamDrawBox_checkBox.Checked = UpCamera.DrawBox;

            if (DownCamera.Active)
            {
                UpdateDownCameraStatusLabel();
                UpdateUpCameraStatusLabel();
            }
            else
            {
                UpdateDownCameraStatusLabel();
                UpdateUpCameraStatusLabel();
            }
            getDownCamList();
            getUpCamList();
            UpdateDownCamBoxXSizeText();
            UpdateDownCamBoxYSizeText();
            UpdateUpCamBoxXSizeText();
            UpdateUpCamBoxYSizeText();
        }

        // =================================================================================
        // get the devices         
        private void getDownCamList()
        {
            List<string> Devices = DownCamera.GetDeviceList();
            DownCam_comboBox.Items.Clear();
            if (Devices.Count != 0)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    DownCam_comboBox.Items.Add(i.ToString(CultureInfo.InvariantCulture) + ": " + Devices[i]);
                    DisplayText("Device " + i.ToString(CultureInfo.InvariantCulture) + ": " + Devices[i]);
                }
            }
            else
            {
                DownCam_comboBox.Items.Add("----");
                UpdateDownCameraStatusLabel();
                DownCameraStatus_label.Text = "No Cam";
            }
            if (DownCam_comboBox.Items.Contains(Setting.Downcam_Name))
            {
                DownCam_comboBox.SelectedItem = Setting.Downcam_Name;
            }
            else
            {
                DownCam_comboBox.SelectedIndex = 0;  // default to first
            }
            DisplayText("DownCam_comboBox.SelectedIndex= " + DownCam_comboBox.SelectedIndex.ToString(CultureInfo.InvariantCulture));
        }

        // ====
        private void getUpCamList()
        {
            List<string> Devices = UpCamera.GetDeviceList();
            UpCam_comboBox.Items.Clear();
            UpCam_comboBox.Items.Add("-- none --");
            if (Devices.Count != 0)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    UpCam_comboBox.Items.Add(i.ToString(CultureInfo.InvariantCulture) + ": " + Devices[i]);
                    DisplayText("Device " + i.ToString(CultureInfo.InvariantCulture) + ": " + Devices[i]);
                }
            }
            if (UpCam_comboBox.Items.Contains(Setting.Upcam_Name))
            {
                UpCam_comboBox.SelectedItem = Setting.Upcam_Name;
            }
            else
            {
                DisplayText("UpCam_comboBox.SelectedIndex= 0");
                UpCam_comboBox.SelectedIndex = 0;
            }

        }


        // =================================================================================
        private void tabPageSetupCameras_End()
        {
            DownCamera.DrawGrid = false;
            ZGuardOn();
        }

        // =================================================================================
        private void RefreshDownCameraList_button_Click(object sender, EventArgs e)
        {
            getDownCamList();
        }

        // ====
        private void RefreshUpCameraList_button_Click(object sender, EventArgs e)
        {
            getUpCamList();
        }

        private void DoDownCamMaxResolution_checkBox_CheckedChanged()
        {
            // Own routine, since called at startup also
            Setting.DownCam_UseMaxResolution = DownCamMaxResolution_checkBox.Checked;
            DowncamDesiredX_label.Enabled = !DownCamMaxResolution_checkBox.Checked;
            DowncamDesiredY_label.Enabled = !DownCamMaxResolution_checkBox.Checked;
            DownCameraDesiredX_textBox.Enabled = !DownCamMaxResolution_checkBox.Checked;
            DownCameraDesiredY_textBox.Enabled = !DownCamMaxResolution_checkBox.Checked;
        }

        private void DownCamMaxResolution_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            DoDownCamMaxResolution_checkBox_CheckedChanged();
        }

        private void DoUpCamMaxResolution_checkBox_CheckedChanged()
        {
            Setting.UpCam_UseMaxResolution = UpCamMaxResolution_checkBox.Checked;
            UpcamDesiredX_label.Enabled = !UpCamMaxResolution_checkBox.Checked;
            UpcamDesiredY_label.Enabled = !UpCamMaxResolution_checkBox.Checked;
            UpCameraDesiredX_textBox.Enabled = !UpCamMaxResolution_checkBox.Checked;
            UpCameraDesiredY_textBox.Enabled = !UpCamMaxResolution_checkBox.Checked;
        }

        private void UpCamMaxResolution_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            DoUpCamMaxResolution_checkBox_CheckedChanged();
        }


        // =================================================================================

        private void SetDownCameraParameters()
        {
            /*
            double val;
            if (DownCamera.IsRunning())
            {
                UpdateUpCameraStatusLabel();
                UpdateDownCameraStatusLabel();
                if (double.TryParse(DownCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    DownCamera.ZoomFactor = val;
                    DownCamera.Zoom = UpCamZoom_checkBox.Checked;
                }
                else
                {
                    DownCamera.Zoom = false;
                };
            }
            else
            {
                UpdateDownCameraStatusLabel();
            }
            */
        }

        private void SetUpCameraParameters()
        {
            /*
            double val;
            if (UpCamera.IsRunning())
            {
                UpdateDownCameraStatusLabel();
                UpdateUpCameraStatusLabel();
                if (double.TryParse(UpCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    UpCamera.ZoomFactor = val;
                    UpCamera.Zoom = UpCamZoom_checkBox.Checked;
                }
                else
                {
                    UpCamera.Zoom = false;
                };
            }
            else
            {
                UpdateUpCameraStatusLabel();
            }
            */
        }

        private void SetCurrentCameraParameters()
        {
            /*
            double val;
            if (UpCamera.IsRunning())
            {
                UpdateDownCameraStatusLabel();
                UpdateUpCameraStatusLabel();
                if (double.TryParse(UpCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    UpCamera.ZoomFactor = val;
                    UpCamera.Zoom = UpCamZoom_checkBox.Checked;
                }
                else
                {
                    UpCamera.Zoom = false;
                };
            }
            else if (DownCamera.IsRunning())
            {
                UpdateUpCameraStatusLabel();
                UpdateDownCameraStatusLabel();
                if (double.TryParse(DownCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    DownCamera.ZoomFactor = val;
                    DownCamera.Zoom = UpCamZoom_checkBox.Checked;
                }
                else
                {
                    DownCamera.Zoom = false;
                };
            }
            else
            {
                return;
            };
            */
        }

        // =================================================================================

        private void DownCam_comboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            /*
            List<string> Monikers = DownCamera.GetMonikerStrings();
            if (Monikers.Count == 0)
            {
                DisplayTxt("No cameras");
                Setting.DowncamMoniker = "-no camera-";
                DownCamera.MonikerString = "-no camera-";
                return;
            }
            Setting.Downcam_Name = DownCam_comboBox.SelectedItem.ToString();
            Setting.DowncamMoniker = Monikers[DownCam_comboBox.SelectedIndex];
            DownCamera.MonikerString = Monikers[DownCam_comboBox.SelectedIndex];
            */
        }

        private void ConnectDownCamera_button_Click(object sender, EventArgs e)
        {
            Do_ConnectDownCamera_button_Click();
        }
        private void Do_ConnectDownCamera_button_Click()
        {
            List<string> Monikers = DownCamera.GetMonikerStrings();
            if (Monikers.Count == 0)
            {
                DisplayText("No cameras");
                Setting.DowncamMoniker = "-no camera-";
                DownCamera.MonikerString = "-no camera-";
                return;
            }
            Setting.Downcam_Name = DownCam_comboBox.SelectedItem.ToString();
            Setting.DowncamMoniker = Monikers[DownCam_comboBox.SelectedIndex];
            DownCamera.MonikerString = Monikers[DownCam_comboBox.SelectedIndex];

            DownCamera.DesiredResolutionX = Setting.DownCam_DesiredX;
            DownCamera.DesiredResolutionY = Setting.DownCam_DesiredY;

            // StartDownCamera_m();
            SelectCamera(DownCamera);
            if (DownCamera.IsRunning())
            {
                SetDownCameraParameters();
            }
            else
            {
                ShowMessageBox(
                    "Problem starting this camera",
                    "Problem starting camera",
                    MessageBoxButtons.OK);
            }
            UpdateUpCameraStatusLabel();
            UpdateDownCameraStatusLabel();
        }

        // ====
        private void UpCam_comboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            /*
            */
        }

        private void ConnectUpCamera_button_Click(object sender, EventArgs e)
        {
            int index = UpCam_comboBox.SelectedIndex;   // remember what is selected now
            getUpCamList(); // Make sure that camera is still there
            if (index>= UpCam_comboBox.Items.Count)
            {
                // No cameras or the last in list went away
                DisplayText("Camera list changed, please re-select", KnownColor.DarkRed, true);
                return;
            }
            UpCam_comboBox.SelectedIndex = index;
            List<string> Monikers = new List<string>();
            Monikers.Add("-no camera-");
            Monikers.AddRange(UpCamera.GetMonikerStrings());
            if (Monikers.Count == 1)
            {
                DisplayText("No cameras");
                Setting.UpcamMoniker = "-no camera-";
                UpCamera.MonikerString = "-no camera-";
                return;
            }
            Setting.Upcam_Name = UpCam_comboBox.SelectedItem.ToString();
            Setting.UpcamMoniker = Monikers[UpCam_comboBox.SelectedIndex];
            UpCamera.MonikerString = Monikers[UpCam_comboBox.SelectedIndex];

            UpCamera.DesiredResolutionX = Setting.UpCam_DesiredX;
            UpCamera.DesiredResolutionY = Setting.UpCam_DesiredY;

            // StartUpCamera_m();
            SelectCamera(UpCamera);
            if (UpCamera.IsRunning())
            {
                SetUpCameraParameters();
            }
            else
            {
                ShowMessageBox(
                    "Problem starting this camera",
                    "Problem starting camera",
                    MessageBoxButtons.OK);
            }
            UpdateUpCameraStatusLabel();
            UpdateDownCameraStatusLabel();
        }

        // =================================================================================
        private bool BoxSizeCalculationValid_m(Camera cam)
        {
            if ((cam.CameraResolution.X == 0) || (cam.CameraResolution.Y == 0))
            {
                DisplayText("camera resolution not set.", KnownColor.DarkRed, true);
                return false;
            }
            return true;
        }
        // =================================================================================
        // Down Camera, X size
        private bool NoRecalc = false;

        private double BoxXzize(Camera cam)
        {
            // How many pixels are really on screen?
            double BoxX = (double)cam.BoxSizeX * (double)cam.CameraResolution.X / (double)cam.DisplayResolution.X;
            if (cam.ShowPixels)
            {
                BoxX = (double)cam.BoxSizeX;
            }
            if (cam.ZoomIsOn)
            {
                BoxX = BoxX / cam.ZoomFactor;
            }
            return BoxX;
        }

        private void DownCameraBoxX_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            if (!BoxSizeCalculationValid_m(DownCamera))
            {
                return;
            }
            double val;
            if (double.TryParse(DownCameraBoxX_textBox.Text.Replace(',', '.'), out val))
            {
                DownCameraBoxX_textBox.ForeColor = Color.Black;
                NoRecalc = true;
                // How many pixels are really on screen?
                double BoxX = BoxXzize(DownCamera);
                Setting.DownCam_XmmPerPixel = val / BoxX;
                DownCamera.XmmPerPixel = val / BoxX;
                DownCameraXmmPerPixel_textBox.Text = Setting.DownCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
                NoRecalc = false;
            }
            else
            {
                DownCameraBoxX_textBox.ForeColor = Color.Red;
            }
        }

        private void DownCameraXmmPerPixel_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            double val;
            if (double.TryParse(DownCameraXmmPerPixel_textBox.Text.Replace(',', '.'), out val))
            {
                NoRecalc = true;
                DownCameraXmmPerPixel_textBox.ForeColor = Color.Black;
                Setting.DownCam_XmmPerPixel = val;
                DownCamera.XmmPerPixel = val;
                UpdateDownCamBoxXSizeText();
                NoRecalc = false;
            }
            else
            {
                DownCameraXmmPerPixel_textBox.ForeColor = Color.Red;
            }
        }

 
        private void UpdateDownCamBoxXSizeText()
        {
            // Own function because the box size needs to be updated when zoom or show pixels status changes
            if (!BoxSizeCalculationValid_m(DownCamera))
            {
                return;
            }
            DownCameraBoxX_textBox.Text = (Setting.DownCam_XmmPerPixel * BoxXzize(DownCamera)).ToString("0.000", CultureInfo.InvariantCulture);
        }

        // =================================================================================
        // Down Camera, Y size
        private double BoxYzize(Camera cam)
        {
            // How many pixels are really on screen?
            double BoxY = (double)cam.BoxSizeY * (double)cam.CameraResolution.Y / (double)cam.DisplayResolution.Y;
            double InputAspectRatio = (double)cam.CameraResolution.Y / (double)cam.CameraResolution.X;
            double OutputAspectRatio = (double)cam.DisplayResolution.Y / (double)cam.DisplayResolution.X;
            BoxY = BoxY * OutputAspectRatio / InputAspectRatio;
            if (cam.ShowPixels)
            {
                BoxY = (double)cam.BoxSizeY;
            }
            if (cam.ZoomIsOn)
            {
                BoxY = BoxY / cam.ZoomFactor;
            }
            return BoxY;
        }

        private void DownCameraBoxY_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            if (!BoxSizeCalculationValid_m(DownCamera))
            {
                return;
            }
            double val;
            if (double.TryParse(DownCameraBoxY_textBox.Text.Replace(',', '.'), out val))
            {
                DownCameraBoxY_textBox.ForeColor = Color.Black;
                NoRecalc = true;
                // How many pixels are really on screen?
                double BoxY = BoxYzize(DownCamera);
                Setting.DownCam_YmmPerPixel = val / BoxY;
                DownCamera.YmmPerPixel = val / BoxY;
                DownCameraYmmPerPixel_textBox.Text = Setting.DownCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
                NoRecalc = false;
            }
            else
            {
                DownCameraBoxY_textBox.ForeColor = Color.Red;
            }
        }


        private void DownCameraYmmPerPixel_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            double val;
            if (double.TryParse(DownCameraYmmPerPixel_textBox.Text.Replace(',', '.'), out val))
            {
                NoRecalc = true;
                DownCameraYmmPerPixel_textBox.ForeColor = Color.Black;
                Setting.DownCam_YmmPerPixel = val;
                DownCamera.YmmPerPixel = val;
                UpdateDownCamBoxYSizeText();
                NoRecalc = false;
            }
            else
            {
                DownCameraYmmPerPixel_textBox.ForeColor = Color.Red;
            }
        }
        private void UpdateDownCamBoxYSizeText()
        {
            // Own function because the box size needs to be updated when zoom or show pixels status changes
            if (!BoxSizeCalculationValid_m(DownCamera))
            {
                return;
            }
            DownCameraBoxY_textBox.Text = (Setting.DownCam_YmmPerPixel * BoxYzize(DownCamera)).ToString("0.000", CultureInfo.InvariantCulture);
        }

        // =================================================================================
        // Up Camera, X size

        private void UpCameraBoxX_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            if (!BoxSizeCalculationValid_m(UpCamera))
            {
                return;
            }
            double val;
            if (double.TryParse(UpCameraBoxX_textBox.Text.Replace(',', '.'), out val))
            {
                UpCameraBoxX_textBox.ForeColor = Color.Black;
                NoRecalc = true;
                // How many pixels are really on screen?
                double BoxX = BoxXzize(UpCamera);
                Setting.UpCam_XmmPerPixel = val / BoxX;
                UpCamera.XmmPerPixel = val / BoxX;
                UpCameraXmmPerPixel_textBox.Text = Setting.UpCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
                NoRecalc = false;
            }
            else
            {
                UpCameraBoxX_textBox.ForeColor = Color.Red;
            }
        }

        private void UpCameraXmmPerPixel_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            double val;
            if (double.TryParse(UpCameraXmmPerPixel_textBox.Text.Replace(',', '.'), out val))
            {
                NoRecalc = true;
                UpCameraXmmPerPixel_textBox.ForeColor = Color.Black;
                Setting.UpCam_XmmPerPixel = val;
                UpCamera.XmmPerPixel = val;
                UpdateUpCamBoxXSizeText();
                NoRecalc = false;
            }
            else
            {
                UpCameraXmmPerPixel_textBox.ForeColor = Color.Red;
            }
        }
        private void UpdateUpCamBoxXSizeText()
        {
            // Own function because the box size needs to be updated when zoom or show pixels status changes
            if (!BoxSizeCalculationValid_m(UpCamera))
            {
                return;
            }
            UpCameraBoxX_textBox.Text = (Setting.UpCam_XmmPerPixel * BoxXzize(UpCamera)).ToString("0.000", CultureInfo.InvariantCulture);
        }

        // =================================================================================
        // Up Camera, Y size

        private void UpCameraBoxY_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            if (!BoxSizeCalculationValid_m(UpCamera))
            {
                return;
            }
            double val;
            if (double.TryParse(UpCameraBoxY_textBox.Text.Replace(',', '.'), out val))
            {
                UpCameraBoxY_textBox.ForeColor = Color.Black;
                NoRecalc = true;
                // How many pixels are really on screen?
                double BoxY = BoxYzize(UpCamera);
                Setting.UpCam_YmmPerPixel = val / BoxY;
                UpCamera.YmmPerPixel = val / BoxY;
                UpCameraYmmPerPixel_textBox.Text = Setting.UpCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture);
                NoRecalc = false;
            }
            else
            {
                UpCameraBoxY_textBox.ForeColor = Color.Red;
            }
        }


        private void UpCameraYmmPerPixel_textBox_TextChanged(object sender, EventArgs e)
        {
            if (NoRecalc || StartingUp)
            {
                return;
            }
            double val;
            if (double.TryParse(UpCameraYmmPerPixel_textBox.Text.Replace(',', '.'), out val))
            {
                NoRecalc = true;
                UpCameraYmmPerPixel_textBox.ForeColor = Color.Black;
                Setting.UpCam_YmmPerPixel = val;
                UpCamera.YmmPerPixel = val;
                UpdateUpCamBoxYSizeText();
                NoRecalc = false;
            }
            else
            {
                UpCameraYmmPerPixel_textBox.ForeColor = Color.Red;
            }
        }
        private void UpdateUpCamBoxYSizeText()
        {
            // Own function because the box size needs to be updated when zoom or show pixels status changes
            if (!BoxSizeCalculationValid_m(UpCamera))
            {
                return;
            }
            UpCameraBoxY_textBox.Text = (Setting.UpCam_YmmPerPixel * BoxYzize(UpCamera)).ToString("0.000", CultureInfo.InvariantCulture);
        }

        // =================================================================================
        private void DownCamZoom_checkBox_Click(object sender, EventArgs e)
        {
            if (DownCamZoom_checkBox.Checked)
            {
                DownCamera.ZoomIsOn = true;
                Setting.DownCam_Zoom = true;
            }
            else
            {
                DownCamera.ZoomIsOn = false;
                Setting.DownCam_Zoom = false;
            }
            UpdateDownCamBoxXSizeText();
            UpdateDownCamBoxYSizeText();
        }
        // ====
        private void UpCamZoom_checkBox_Click(object sender, EventArgs e)
        {
            if (UpCamZoom_checkBox.Checked)
            {
                UpCamera.ZoomIsOn = true;
                Setting.UpCam_Zoom = true;
            }
            else
            {
                UpCamera.ZoomIsOn = false;
                Setting.UpCam_Zoom = false;
            }
            UpdateUpCamBoxXSizeText();
            UpdateUpCamBoxYSizeText();
        }

        // =================================================================================
        private void DownCamZoomFactor_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(DownCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
            {
                if (val < 1.0)
                {
                    DisplayText("Zoom factor must be >1", KnownColor.DarkRed, true);
                    DownCamZoomFactor_textBox.ForeColor = Color.Red;
                }
                else
                {
                    DownCamZoomFactor_textBox.ForeColor = Color.Black;
                    Setting.DownCam_Zoomfactor = val;
                    DownCamera.ZoomFactor = val;
                    UpdateDownCamBoxXSizeText();
                    UpdateDownCamBoxYSizeText();
                }
            }
            else
            {
                DownCamZoomFactor_textBox.ForeColor = Color.Red;
            }
        }

        // ====
        private void UpCamZoomFactor_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(UpCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
            {
                if (val < 1.0)
                {
                    DisplayText("Zoom factor must be >1", KnownColor.DarkRed, true);
                    UpCamZoomFactor_textBox.ForeColor = Color.Red;
                }
                else
                {
                    UpCamZoomFactor_textBox.ForeColor = Color.Black;
                    Setting.UpCam_Zoomfactor = val;
                    UpCamera.ZoomFactor = val;
                    UpdateUpCamBoxXSizeText();
                    UpdateUpCamBoxYSizeText();
                }
            }
            else
            {
                UpCamZoomFactor_textBox.ForeColor = Color.Red;
            }
        }



        // =================================================================================
        // DownCam specific functions
        // =================================================================================
        private void DownCamDrawCross_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            DownCamera.DrawCross = DownCamDrawCross_checkBox.Checked;
            Setting.DownCam_DrawCross = DownCamDrawCross_checkBox.Checked;
        }

        private void DownCamDrawBox_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            DownCamera.DrawBox = DownCamDrawBox_checkBox.Checked;
            Setting.DownCam_DrawBox = DownCamDrawBox_checkBox.Checked;
        }

        private void DownCamDrawSidemarks_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            DownCamera.DrawSidemarks = DownCamDrawSidemarks_checkBox.Checked;
            Setting.DownCam_DrawSidemarks = DownCamDrawSidemarks_checkBox.Checked;
        }

        // =================================================================================
        private void ImageTest_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (ImageTest_checkBox.Checked)
            {
                DownCamera.TestAlgorithm = true;
            }
            else
            {
                DownCamera.TestAlgorithm = false;
            }
        }

        // =================================================================================

        private void JigX_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JigX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_JigOffsetX = val;
                JigX_textBox.ForeColor = Color.Black;
            }
            else
            {
                JigX_textBox.ForeColor = Color.Red;
            }
        }


        private void JigY_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JigY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_JigOffsetY = val;
                JigY_textBox.ForeColor = Color.Black;
            }
            else
            {
                JigY_textBox.ForeColor = Color.Red;
            }
        }

        // =================================================================================
        private void GotoPCB0_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            CNC_XYA_m(Setting.General_JigOffsetX, Setting.General_JigOffsetY, Cnc.CurrentA);
        }

        // =================================================================================
        private void SetPCB0_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            JigX_textBox.Text = Cnc.CurrentX.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_JigOffsetX = Cnc.CurrentX;
            JigY_textBox.Text = Cnc.CurrentY.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_JigOffsetY = Cnc.CurrentY;
        }



        // =================================================================================

        private void PickupCenterX_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(PickupCenterX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_PickupCenterX = val;
                PickupCenterX_textBox.ForeColor = Color.Black;
            }
            else
            {
                PickupCenterX_textBox.ForeColor = Color.Red;
            }
        }


        private void PickupCenterY_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(PickupCenterY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_PickupCenterY = val;
                PickupCenterY_textBox.ForeColor = Color.Black;
            }
            else
            {
                PickupCenterY_textBox.ForeColor = Color.Red;
            }
        }

        private void GotoPickupCenter_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            CNC_XYA_m(Setting.General_PickupCenterX, Setting.General_PickupCenterY, Cnc.CurrentA);
        }

        private void SetPickupCenter_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            PickupCenterX_textBox.Text = Cnc.CurrentX.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_PickupCenterX = Cnc.CurrentX;
            PickupCenterY_textBox.Text = Cnc.CurrentY.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_PickupCenterY = Cnc.CurrentY;
            PickupCenterX_textBox.ForeColor = Color.Black;
            PickupCenterY_textBox.ForeColor = Color.Black;
        }

        // =================================================================================
        // Nozzle calibration

        private static int SetNozzleOffset_stage = 0;
        private static double NozzleOffsetMarkX = 0.0;
        private static double NozzleOffsetMarkY = 0.0;

        private void ZDown_button_Click(object sender, EventArgs e)
        {
            CNC_Z_m(Setting.General_Z0toPCB-0.5);
        }

        private void ZUp_button_Click(object sender, EventArgs e)
        {
            CNC_Z_m(0);
        }

        private void NozzleOffsetX_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleOffsetX_textBox.Text.Replace(',', '.'), out val))
            {
                NozzleOffsetX_textBox.ForeColor = Color.Black;
                Setting.DownCam_NozzleOffsetX = val;
                DisplayText("DownCam_NozzleOffsetX= " + val.ToString("0.000", CultureInfo.InvariantCulture));
            }
            else
            {
                NozzleOffsetX_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleOffsetY_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleOffsetY_textBox.Text.Replace(',', '.'), out val))
            {
                NozzleOffsetY_textBox.ForeColor = Color.Black;
                Setting.DownCam_NozzleOffsetY = val;
                DisplayText("DownCam_NozzleOffsetY= " + val.ToString("0.000", CultureInfo.InvariantCulture));
            }
            else
            {
                NozzleOffsetY_textBox.ForeColor = Color.Red;
            }
        }


        private void NozzleHeightStart_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            // Nozzle calibration button
            ZGuardOff();
            SelectCamera(DownCamera);
            // SetDownCameraParameters();
            switch (SetNozzleOffset_stage)
            {
                case 0:
                    NozzleHeightStart_button.Text = "Next";
                    if (!CNC_A_m(0.0))
                    {
                        break;
                    }
                    NozzleOffset_label.Visible = true;
                    NozzleOffset_label.Text = "Jog Nozzle to a point on a PCB, then click \"Next\"";
                    SetNozzleOffset_stage = 1;
                    NozzleHeightCancel_button.Enabled = true;
                    break;

                case 1:
                    NozzleOffsetMarkX = Cnc.CurrentX;
                    NozzleOffsetMarkY = Cnc.CurrentY;
                    if (!CNC_Z_m(0.0))
                    {
                        break;
                    }
                    if (!CNC_XYA_m(Cnc.CurrentX - Setting.DownCam_NozzleOffsetX, Cnc.CurrentY - Setting.DownCam_NozzleOffsetY, Cnc.CurrentA))
                    {
                        break;
                    }
                    DownCamera.DrawCross = true;
                    NozzleOffset_label.Text = "Jog camera above the same point, \n\rthen click \"Next\"";
                    SetNozzleOffset_stage = 2;
                    break;

                case 2:
                    Setting.DownCam_NozzleOffsetX = NozzleOffsetMarkX - Cnc.CurrentX;
                    Setting.DownCam_NozzleOffsetY = NozzleOffsetMarkY - Cnc.CurrentY;
                    NozzleOffsetX_textBox.Text = Setting.DownCam_NozzleOffsetX.ToString("0.00", CultureInfo.InvariantCulture);
                    NozzleOffsetY_textBox.Text = Setting.DownCam_NozzleOffsetY.ToString("0.00", CultureInfo.InvariantCulture);
                    ShowMessageBox(
                        "Now, jog the Nozzle above the up camera,\n\rtake Nozzle down, jog it to the image center\n\rand set Up Camera location",
                        "Done here",
                        MessageBoxButtons.OK);
                    UpCam_radioButton.Checked = true;
                    ResetNozzleOffsetStateMachine();
                    SelectCamera(UpCamera);
                    // SetNozzleMeasurement();
                    CNC_Z_m(0.0);
                    ZGuardOn();
                    break;
            }
        }

        private void ResetNozzleOffsetStateMachine()
        {
            NozzleHeightStart_button.Text = "Start";
            SetNozzleOffset_stage = 0;
            NozzleOffset_label.Visible = false;
            NozzleOffset_label.Text = "   ";
            NozzleHeightCancel_button.Enabled = false;
        }

        private void NozzleHeightCancel_button_Click(object sender, EventArgs e)
        {
            ResetNozzleOffsetStateMachine();
            CNC_Z_m(0.0);
            ZGuardOn();
        }


        // =================================================================================
        // UpCam specific functions
        // =================================================================================

        private void UpCamDrawCross_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            UpCamera.DrawCross = UpCamDrawCross_checkBox.Checked;
            Setting.UpCam_DrawCross = UpCamDrawCross_checkBox.Checked;
        }

        private void UpCamDrawBox_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            UpCamera.DrawBox = UpCamDrawBox_checkBox.Checked;
            Setting.UpCam_DrawBox = UpCamDrawBox_checkBox.Checked;
        }

        private void UpCamDrawSidemarks_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            UpCamera.DrawSidemarks = UpCamDrawSidemarks_checkBox.Checked;
            Setting.UpCam_DrawSidemarks = UpCamDrawSidemarks_checkBox.Checked;
        }

        private void UpcamPositionX_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(UpcamPositionX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.UpCam_PositionX = val;
                UpcamPositionX_textBox.ForeColor = Color.Black;
            }
            else
            {
                UpcamPositionX_textBox.ForeColor = Color.Red;
            }
        }

        private void UpcamPositionY_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(UpcamPositionY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.UpCam_PositionY = val;
                UpcamPositionY_textBox.ForeColor = Color.Black;
            }
            else
            {
                UpcamPositionY_textBox.ForeColor = Color.Red;
            }
        }

        private void SetUpCamPosition_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            UpcamPositionX_textBox.Text = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Setting.UpCam_PositionX = Cnc.CurrentX;
            UpcamPositionY_textBox.Text = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            Setting.UpCam_PositionY = Cnc.CurrentY;
            DisplayText("True position (with Nozzle offset):");
            DisplayText("X: " + (Cnc.CurrentX - Setting.DownCam_NozzleOffsetX).ToString(CultureInfo.InvariantCulture));
            DisplayText("Y: " + (Cnc.CurrentY - Setting.DownCam_NozzleOffsetY).ToString(CultureInfo.InvariantCulture));
        }

        private void GotoUpCamPosition_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            CNC_XYA_m(Setting.UpCam_PositionX, Setting.UpCam_PositionY, Cnc.CurrentA);
        }

        #endregion Up/Down Camera setup pages functions

        // =================================================================================
        // Basic setup page functions
        // =================================================================================
        #region Basic setup page functions


        private void BasicSetupTab_Begin()
        {
            // SetDownCameraDefaults();
            DisplayText("Basic Setup tab begin");

            // UpCamera.Active = false;
            // DownCamera.Active = false;

            if (!StartingUp)
            {
                UpdateCncConnectionStatus();
            }

            Cnc.SlackCompensation = Setting.CNC_SlackCompensation;
            SlackCompensation_checkBox.Checked = Setting.CNC_SlackCompensation;
            SlackCompensationDistance_textBox.Text = Setting.SlackCompensationDistance.ToString("0.00", CultureInfo.InvariantCulture);
            Cnc.SlackCompensationDistance = Setting.SlackCompensationDistance;

            Cnc.SlackCompensationA = Setting.CNC_SlackCompensationA;
            SlackCompensationA_checkBox.Checked = Setting.CNC_SlackCompensationA;

            MouseScroll_checkBox.Checked = Setting.CNC_EnableMouseWheelJog;
            NumPadJog_checkBox.Checked = Setting.CNC_EnableNumPadJog;

            ZTestTravel_textBox.Text = Setting.General_ZTestTravel.ToString(CultureInfo.InvariantCulture);
            ShadeGuard_textBox.Text = Setting.General_ShadeGuard_mm.ToString(CultureInfo.InvariantCulture);

            PumpInvert_checkBox.Checked = Setting.General_PumpOutputInverted;
            VacuumInvert_checkBox.Checked = Setting.General_VacuumOutputInverted;
            Pump_checkBox.Checked = Cnc.PumpIsOn;
            Vacuum_checkBox.Checked = Cnc.VacuumIsOn;
            MoveTimeout_textBox.Text = Setting.CNC_RegularMoveTimeout.ToString("0.0", CultureInfo.InvariantCulture);

            AutoPark_checkBox.Checked = Setting.General_Autopark;
            OptimizeA_TinyG_checkBox.Checked = Setting.CNC_OptimizeA;
            OptimizeA_Marlin_checkBox.Checked = Setting.CNC_OptimizeA;

            SizeXMax_textBox.Text = Setting.General_MachineSizeX.ToString(CultureInfo.InvariantCulture);
            SizeYMax_textBox.Text = Setting.General_MachineSizeY.ToString(CultureInfo.InvariantCulture);

            NegativeMoveX_textBox.Text = Setting.General_NegativeX.ToString(CultureInfo.InvariantCulture);
            NegativeMoveY_textBox.Text = Setting.General_NegativeY.ToString(CultureInfo.InvariantCulture);

            ParkLocationX_textBox.Text = Setting.General_ParkX.ToString(CultureInfo.InvariantCulture);
            ParkLocationY_textBox.Text = Setting.General_ParkY.ToString(CultureInfo.InvariantCulture);
            SquareCorrection_textBox.Text = Setting.CNC_SquareCorrection.ToString(CultureInfo.InvariantCulture);
            VacuumTime_textBox.Text = Setting.General_PickupVacuumTime.ToString(CultureInfo.InvariantCulture);
            VacuumRelease_textBox.Text = Setting.General_PickupReleaseTime.ToString(CultureInfo.InvariantCulture);
            SmallMovement_numericUpDown.Value = Setting.CNC_SmallMovementSpeed;

            CtlrJogSpeed_numericUpDown.Value = Setting.CNC_CtlrJogSpeed;
            NormalJogSpeed_numericUpDown.Value = Setting.CNC_NormalJogSpeed;
            AltJogSpeed_numericUpDown.Value = Setting.CNC_AltJogSpeed;

            NozzleBelowPCB_textBox.Text = Setting.General_BelowPCB_Allowance.ToString(CultureInfo.InvariantCulture);
            if (Setting.NozzleHeightSetupStage != 0)
            {
                Cancel_TestSwitchClearanceOp();
            }
            TestSwitchClearanceCancel_button.Visible = false;
            TestSwitchClearance_button.Text = "Start";
            PickupDepth_textBox.Text = Setting.Placement_Pickup_Depth.ToString("0.00", CultureInfo.InvariantCulture);
            PlacementDepth_textBox.Text = Setting.Placement_Placement_Depth.ToString("0.00", CultureInfo.InvariantCulture);
            if (Setting.General_HeightCalibrationDone)
            {
                //Z_SwitchClearance_textBox.Text = Setting.CNC_ZswitchClearance.ToString("0.00", CultureInfo.InvariantCulture);
                Z0toPCB_textBox.Text = Setting.General_Z0toPCB.ToString("0.00", CultureInfo.InvariantCulture);
                TouchDifference_textBox.Text = Setting.General_ZTouchDifference.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else 
            {
                Z0toPCB_textBox.Text = "";
                TouchDifference_textBox.Text = "";
                PlacementDepth_textBox.Text = "";
            }
            // Does this machine have any ports? (Maybe not, if TinyG is powered down.) 
            RefreshPortList();
            if (comboBoxSerialPorts.Items.Count == 0)
            {
                return;
            };

            // At least there are some ports. Show the default port, if it is still there:
            bool found = false;
            int portno = 0;
            foreach (var item in comboBoxSerialPorts.Items)
            {
                if (item.ToString() == Setting.CNC_SerialPort)
                {
                    found = true;
                    comboBoxSerialPorts.SelectedIndex = portno;
                    break;
                }
                portno++;
            }
            if (found)
            {
                // Yes, the default port is still there, show it
                comboBoxSerialPorts.SelectedIndex = portno;
            }
            else
            {
                // show the first available port
                comboBoxSerialPorts.SelectedIndex = 0;
                return;
            }
        }

        private void BasicSetupTab_End()
        {
            ZGuardOn();
        }

        private void ButtonRefreshPortList_Click(object sender, EventArgs e)
        {
            RefreshPortList();
        }

        private void RefreshPortList()
        {
            comboBoxSerialPorts.Items.Clear();
            foreach (string s in SerialPort.GetPortNames())
            {
                comboBoxSerialPorts.Items.Add(s);
            }
            if (comboBoxSerialPorts.Items.Count == 0)
            {
                labelSerialPortStatus.Text = "No serial ports found.\n\rIs control board powered on?";
            }
            else
            {
                if (Cnc.Connected)
                {
                    // does the port that cnc think it is connected to still exist?
                    for (int i = 0; i < comboBoxSerialPorts.Items.Count; i++)
                    {
                        if (comboBoxSerialPorts.Items[i].ToString() == Cnc.Port)
                        {
                            comboBoxSerialPorts.SelectedIndex = i;
                            UpdateCncConnectionStatus();
                            return;
                        }
                    }
                    Cnc.ClosePort();
                    UpdateCncConnectionStatus();
                }
                else
                {
                    // show the first available port
                    comboBoxSerialPorts.SelectedIndex = 0;
                    UpdateCncConnectionStatus();
                }
            }
        }

        public void CncError()
        {
            Cnc.ErrorState = true;
            UpdateCncConnectionStatus();
            ResetNozzleOffsetStateMachine();
        }

        public void UpdateCncConnectionStatus()
        {
            if (InvokeRequired) { Invoke(new Action(UpdateCncConnectionStatus)); return; }

            if (StartingUp)
            {
                return;
            }
            // Possible states:
            // - no connection tried
            // - serial port not ok
            // - serial port ok, cnc board is not
            // - connected to board

            if (Setting.CNC_SerialPort == "")
            {
                // - no connection tried
            }

            if (!Cnc.Connected)
            {
                // - serial port not ok
                PositionConfidence = false;
                OpticalHome_button.BackColor = Color.Red;
                buttonConnectSerial.Text = "Connect";
                labelSerialPortStatus.Text = "Not connected";
                labelSerialPortStatus.ForeColor = Color.Red;
                ValidMeasurement_checkBox.Checked = false;
                return;
            }

            if (Cnc.ErrorState)
            {
                // - serial port ok, cnc board is not
                buttonConnectSerial.Text = "Clear Err.";
                labelSerialPortStatus.Text = "Connected, control board in error state";
                labelSerialPortStatus.ForeColor = Color.Red;
                ValidMeasurement_checkBox.Checked = false;
                PositionConfidence = false;
                OpticalHome_button.BackColor = Color.Red;
            }
            else
            {
                buttonConnectSerial.Text = "Close";
                labelSerialPortStatus.Text = "Connected to " + Cnc.Port;
                labelSerialPortStatus.ForeColor = Color.Black;
            }
        }

        private void buttonConnectSerial_Click(object sender, EventArgs e)
        {
            if (comboBoxSerialPorts.SelectedItem == null)
            {
                return;  // no ports
            };
            ConnectToCnc(comboBoxSerialPorts.SelectedItem.ToString());
        }

        private bool MessageShown = false;
        private bool CNCstarting = false;

        private void ConnectToCnc(string port)
        {
            Motors_label.Text = "Control board not connected.";
            TinyGMotors_tabControl.Visible = false;
            MarlinMotors_tabControl.Visible = false;


            // When first starting, there is no default port. Trying to connect to a random port is 
            // potentially harmful, so we show a message. Once a message is shown, it is ok to try
            if (port == "")
            {
                if (!MessageShown)
                {
                    NoPort_label.Visible = true;
                    SaveAlldata_button.Visible = false;  // to save real estate on screen
                    MessageShown = true;
                    UpdateCncConnectionStatus();
                    return;         // return, as showing the no default port label is the only thing to do
                }
                if (comboBoxSerialPorts.Items.Count == 0)
                {
                    UpdateCncConnectionStatus();
                    return;     // return to have no status change after there are some ports
                }
            }

            NoPort_label.Visible = false;
            SaveAlldata_button.Visible = true;
            // User wants close current connection (the button is a toggle) or (re-)connect
            bool JustClose = (Cnc.Connected && !Cnc.ErrorState);

            // Close existing connection always
            buttonConnectSerial.Text = "Closing...";

            Cnc.ClosePort();

            if (JustClose)      // user didn't want to connect (why?)
            {
                UpdateCncConnectionStatus();
                return;
            }

            // Possible connections are now closed and we know user wants to connect:
            buttonConnectSerial.Text = "Connecting...";
            if (comboBoxSerialPorts.SelectedItem == null)
            {
                CncError();
                Cnc.Connected = false;
                UpdateCncConnectionStatus();
                return;
            }
            if (!Cnc.Connect(comboBoxSerialPorts.SelectedItem.ToString()))
            {
                CncError();
                Cnc.Connected = false;
                UpdateCncConnectionStatus();
                return;
            }

            // Connection established
            Setting.CNC_SerialPort = comboBoxSerialPorts.SelectedItem.ToString();
            Cnc.ErrorState = false;
            UI_BoardConnected();
            if (!Cnc.JustConnected())  // does board specific startup things
            {
                CncError();
                UpdateCncConnectionStatus();
                return;
            }
            UpdateCncConnectionStatus();
            // Board specific UI stuff

            // System stuff after board connection
            CheckLatchBackoff();
            Cnc.PumpDefaultSetting();
            Cnc.VacuumDefaultSetting();
            OfferHoming();

        }

        private void UI_BoardConnected()
        {
            switch (Setting.Controlboard)
            {
                case ControlBoardType.TinyG:
                    Motors_label.Text = "Axes setup (TinyG board):";
                    MarlinMotors_tabControl.Visible = false;
                    TinyGMotors_tabControl.Visible = true;
                    return;
                case ControlBoardType.Marlin:
                    Motors_label.Text = "Axes setup (Duet 3 board):";
                    TinyGMotors_tabControl.Visible = false;
                    MarlinMotors_tabControl.Visible = true;
                    return;
                case ControlBoardType.unknown:
                    Motors_label.Text = "Connected to unknown board";
                    TinyGMotors_tabControl.Visible = false;
                    MarlinMotors_tabControl.Visible = false;
                    return;
                case ControlBoardType.other:            // should not happen
                    Motors_label.Text = "Connected to \"other\" type board";
                    TinyGMotors_tabControl.Visible = false;
                    MarlinMotors_tabControl.Visible = false;
                    return;
                default:            // should not happen
                    Motors_label.Text = "Connected to unknown (default) board";
                    TinyGMotors_tabControl.Visible = false;
                    MarlinMotors_tabControl.Visible = false;
                    return;        // should not happen
            }


        }
        public  void CheckLatchBackoff()
        {
            if (Setting.Controlboard == ControlBoardType.TinyG)
            {
                double val;
                if (!double.TryParse(TinyGBoard.Zlb.ToString().Replace(',', '.'), out val))
                {
                    DisplayText("Bad data at latch backoff!", KnownColor.DarkRed, true);
                    return;
                }

                if ((val < 5) && !Setting.General_ZlbFixAsked)
                {
                    DisplayText("Open latch backoff dialog", KnownColor.DarkGreen, true);
                    LatchBackoffForm LatchBackoffDialog = new LatchBackoffForm();
                    LatchBackoffDialog.MainForm = this;
                    LatchBackoffDialog.zlb_now = TinyGBoard.Zlb;
                    LatchBackoffDialog.StartPosition = FormStartPosition.CenterParent;
                    LatchBackoffDialog.ShowDialog(this);
                }

            }
        }
        // =================================================================================
        // Logging textbox

        // Steal ctrl+C to copy with colors
        private void SerialMonitor_richTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if ((e.KeyChar == 0x03) && ((ModifierKeys & Keys.Control) == Keys.Control))
            {
                SetClipboardText(true);
                e.Handled = true;
            }
            if ((e.KeyChar == 0x04) && ((ModifierKeys & Keys.Control) == Keys.Control))
            {
                SetClipboardText(false);
                e.Handled = true;
            }
        }

        private void SetClipboardText(bool RichText)
        {
            if (InvokeRequired) { Invoke(new Action<bool>(SetClipboardText), new[] { RichText }); return; }
 
            DataObject dto = new DataObject();
            if (RichText)
            {
                dto.SetText(SerialMonitor_richTextBox.SelectedRtf, TextDataFormat.Rtf);
            }
            else
            {
                dto.SetText(SerialMonitor_richTextBox.Text, TextDataFormat.Text);
            }
            Clipboard.Clear();
            Clipboard.SetDataObject(dto);
        }

        Color DisplayTxtCol = Color.Black;

        public void DisplayText(string txt, KnownColor col = KnownColor.Black, bool force = false)
        {
            if (DisableLog_checkBox.Checked && !force)
            {
                return;
            }
            DisplayTxtCol = Color.FromKnownColor(col);
            _DisplayTxt(txt);
        }

        public void _DisplayTxt(string txt)
        {
            if (InvokeRequired) { Invoke(new Action<string>(_DisplayTxt), new[] { txt }); return; }

            try
            {
                txt = txt.Replace("\n", "");
                txt = txt.Replace("\r", "");
                // TinyG sends \n, textbox needs \r\n. (TinyG could be set to send \n\r, which does not work with textbox.)
                // Adding end of line here saves typing elsewhere
                txt = txt + "\r\n";
                if (SerialMonitor_richTextBox.Text.Length > 1000000)
                {
                    SerialMonitor_richTextBox.Text = SerialMonitor_richTextBox.Text.Substring(SerialMonitor_richTextBox.Text.Length - 10000);
                }
                SerialMonitor_richTextBox.AppendText(txt, DisplayTxtCol);

            }
            catch (ObjectDisposedException)
            {
                return;     // exit during startup
            }
        }

        // =================================================================================

        private void SendtoControlBoard_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                Cnc.ForceWrite(SendtoControlBoard_textBox.Text);
                SendtoControlBoard_textBox.Clear();
                e.Handled = true;   // othewrwise, a ding sound
            }
        }


        // =========================================================================
        #region Machine_Size

        private bool SetXsize_m(double size)
        {
            if (StartingUp)
            {
                return true;
            };

            bool res = Cnc.SetMachineSizeX((int)Math.Round(size));
            if (!res)
            {
                ShowMessageBox(
                    "The new size was not written to control board. \n\r" +
                    "Fix the communications and try again.",
                    "Write failed",
                    MessageBoxButtons.OK);
            }
            return res;
        }


        private void SizeXMax_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            SizeXMax_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                double val;
                if (double.TryParse(SizeXMax_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_MachineSizeX = val;
                    if (SetXsize_m(val))
                    {
                        SizeXMax_textBox.ForeColor = Color.Black;
                    }
                }
                else
                {
                    DisplayText("Value did not convert to a number.");
                }
                e.Handled = true;   // supress the ding sound
            }
        }


        private bool SetYsize_m(double size)
        {
            if (StartingUp)
            {
                return true;
            };

            bool res = Cnc.SetMachineSizeY((int)Math.Round(size));
            if (!res)
            {
                ShowMessageBox(
                    "The new size was not written to control board. \n\r" +
                    "Fix the communications and try again.",
                    "Write failed",
                    MessageBoxButtons.OK);
            }
            return res;
        }

        private void SizeYMax_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            SizeYMax_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                double val;
                if (double.TryParse(SizeYMax_textBox.Text.Replace(',', '.'), out val))
                {
                   Setting.General_MachineSizeY = val;
                   if (SetYsize_m(val))
                    {
                        SizeYMax_textBox.ForeColor = Color.Black;
                    }
                }
                else
                {
                    DisplayText("Value did not convert to a number.");
                }
                e.Handled = true;   // supress the ding sound
            }
        }
        #endregion

        // =========================================================================
        #region park_location
        private void ParkLocationX_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ParkLocationX_textBox.Text.Replace(',', '.'), out val))
            {
                ParkLocationX_textBox.ForeColor = Color.Black;
                Setting.General_ParkX = val;
            }
            else
            {
                ParkLocationX_textBox.ForeColor = Color.Red;
            }
        }


        private void ParkLocationY_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ParkLocationY_textBox.Text.Replace(',', '.'), out val))
            {
                ParkLocationY_textBox.ForeColor = Color.Black;
                Setting.General_ParkY = val;
            }
            else
            {
                ParkLocationY_textBox.ForeColor = Color.Red;
            }
        }

        private void Park_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            CNC_Park();
        }

        #endregion

        // =========================================================================

        private void reset_button_Click(object sender, EventArgs e)
        {
            Cnc.Write_m("\x18");
        }

        #region MovementTestButtons

        private void TestX_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            if (Cnc.CurrentX < 1.0)
            {
                CNC_XYA_m(Setting.General_MachineSizeX, Cnc.CurrentY, Cnc.CurrentA);
            }
            else
            {
                CNC_XYA_m(0.0, Cnc.CurrentY, Cnc.CurrentA);
            }
        }

        private void TestY_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            if (Cnc.CurrentY < 1.0)
            {
                CNC_XYA_m(Cnc.CurrentX, Setting.General_MachineSizeY, Cnc.CurrentA);
            }
            else
            {
                CNC_XYA_m(Cnc.CurrentX, 0.0, Cnc.CurrentA);
            }
        }


        private void TestXYA_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            if ((Cnc.CurrentX < 1.0) && (Cnc.CurrentY < 1.0))
            {
                CNC_XYA_m(Setting.General_MachineSizeX, Setting.General_MachineSizeY, 360.0);
            }
            else
            {
                CNC_XYA_m(0, 0, 0);
            }
        }

        private void TestXY_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            if ((Cnc.CurrentX < 1.0) && (Cnc.CurrentY < 1.0))
            {
                CNC_XYA_m(Setting.General_MachineSizeX, Setting.General_MachineSizeY, Cnc.CurrentA);
            }
            else
            {
                CNC_XYA_m(0, 0, Cnc.CurrentA);
            }
        }

        private void TestYX_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            if ((Cnc.CurrentX > (Setting.General_MachineSizeX - 1.0)) && (Cnc.CurrentY < 1.0))
            {
                CNC_XYA_m(0.0, Setting.General_MachineSizeY, Cnc.CurrentA);
            }
            else
            {
                CNC_XYA_m(Setting.General_MachineSizeX, 0.0, Cnc.CurrentA);
            }
        }

        private void TestZ_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            if (Cnc.CurrentZ < 1.0)
            {
                CNC_Z_m(Setting.General_ZTestTravel);
            }
            else
            {
                CNC_Z_m(0);
            }
        }

        private void ZTestTravel_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ZTestTravel_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_ZTestTravel = val;
            }
        }

        private void ShadeGuard_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ShadeGuard_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_ShadeGuard_mm = val;
            }
        }


        private void TestA_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            bool OptSave = Setting.CNC_OptimizeA;
            Setting.CNC_OptimizeA = false;
            if (!CNC_A_m(0))
            {
                Setting.CNC_OptimizeA = OptSave;
                return;
            }
            if (!CNC_A_m(360))
            {
                Setting.CNC_OptimizeA = OptSave;
                return;
            }
            CNC_A_m(0);
            Setting.CNC_OptimizeA = OptSave;
            return;
        }

        #endregion

        #region HomingButtons

        private bool HomeX_m()
        {
            if (MovementIsBusy_m())
            {
                return false;
            }
            if (Setting.Controlboard == ControlBoardType.TinyG)
            {
                if (!Xhome_checkBox.Checked)
                {
                    MainForm.DisplayText("X homing switch not enabled", KnownColor.DarkRed, true);
                    return false;
                }
            }
            Homing = true;
            bool res = Cnc.Home_m("X");
            Homing = false;
            return res;
        }

        private bool HomeY_m()
        {
            if (MovementIsBusy_m())
            {
                return false;
            }
            if (Setting.Controlboard == ControlBoardType.TinyG)
            {
                if (!Yhome_checkBox.Checked)
                {
                    MainForm.DisplayText("Y homing switch not enabled", KnownColor.DarkRed, true);
                    return false;
                }
            }
            Homing = true;
            bool res = Cnc.Home_m("Y");
            Homing = false;
            return res;
        }

        private bool Check_zzb()
        {
            if (TinyGBoard.Zzb == "0.000")
            {
                DisplayText("Z probing value in TinyG = 0. Error during height calibration/measurement?", KnownColor.DarkRed, true);
                DisplayText("To fix, send text: {\"zzb\",2.0}", KnownColor.DarkRed, true);
                DisplayText("(Or if you have changed the value, replace 2.0 with your number)", KnownColor.DarkRed, true);
                return false;
            }
            else
            {
                return true;
            }
        }

        public bool HomeZ_m()
        {
            if (MovementIsBusy_m())
            {
                return false;
            }
            if (Setting.Controlboard == ControlBoardType.TinyG)
            {
                if (LastTabPage == "Nozzles_tabPage")
                {
                    // On nozzles page, switches are disabled. Easiest is to do homing on basic setup page
                    tabControlPages.SelectedTab = tabControlPages.TabPages["tabPageBasicSetup"];
                }
                if (!Zhome_checkBox.Checked)
                {
                    DisplayText("Z homing switch not enabled.\n\r" +
                        "(Abort or crash during probing or nozzle setup? Please re-enable.)", KnownColor.DarkRed, true);
                    return false;
                }
                if (!Check_zzb())
                {
                    return false;
                }
            }

            Homing = true;
            bool res = Cnc.Home_m("Z");
            Homing = false;
            return res;
        }

        private void HomeX_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }

            HomeX_m();
        }

        private void HomeY_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }

            HomeY_m();
        }

        private void HomeZ_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }

            HomeZ_m();
        }


        private void HomeXY_button_Click(object sender, EventArgs e)
        {
            if (!HomeX_m())
                return;
            HomeY_m();
        }


        private void HomeXYZ_button_Click(object sender, EventArgs e)
        {
            if (!HomeZ_m())
                return;
            if (!HomeX_m())
                return;
            if (!HomeY_m())
                return;
            CNC_A_m(0);
        }

        #endregion

        private void MotorPower_checkBox_Click(object sender, EventArgs e)
        {
            if (MotorPower_checkBox.Checked)
            {
                Cnc.MotorPowerOn();
            }
            else
            {
                Cnc.MotorPowerOff();
            }
            PositionConfidence = false;
            OpticalHome_button.BackColor = Color.Red;
            DisplayText("*** Switching motor power causes loss of precise position. ", KnownColor.DarkRed, true);
            DisplayText("Re-homing is needed.", KnownColor.DarkRed, true);
        }

        // =======================================
        private void PumpOn()
        {
            if (Setting.General_PumpOutputInverted)
            {
                Cnc.Pump_Off();
            }
            else
            {
                Cnc.Pump_On();
            }
            Pump_checkBox.Checked = true;
        }

        private void PumpOff()
        {
            if (Setting.General_PumpOutputInverted)
            {
                Cnc.Pump_On();
            }
            else
            {
                Cnc.Pump_Off();
            }
            Pump_checkBox.Checked = false;
        }

        private void Pump_checkBox_Click(object sender, EventArgs e)
        {
            if (Pump_checkBox.Checked)
            {
                PumpOn();
            }
            else
            {
                PumpOff();
            }
        }

        private void PumpInvert_checkBox_Click(object sender, EventArgs e)
        {
            Setting.General_PumpOutputInverted = PumpInvert_checkBox.Checked;
        }

        // =======================================
        private void VacuumOn()
        {
            if (Setting.General_VacuumOutputInverted)
            {
                Cnc.Vacuum_Off();
            }
            else
            {
                Cnc.Vacuum_On();
            }
            Vacuum_checkBox.Checked = true;
            Thread.Sleep(Setting.General_PickupVacuumTime);
        }

        private void VacuumOff()
        {
            if (Setting.General_VacuumOutputInverted)
            {
                Cnc.Vacuum_On();
            }
            else
            {
                Cnc.Vacuum_Off();
            }
            Vacuum_checkBox.Checked = false;
            Thread.Sleep(Setting.General_PickupReleaseTime);
        }

        private void Vacuum_checkBox_Click(object sender, EventArgs e)
        {
            if (Vacuum_checkBox.Checked)
            {
                VacuumOn();
            }
            else
            {
                VacuumOff();
            }
        }



        private void VacuumInvert_checkBox_Click(object sender, EventArgs e)
        {
            Setting.General_VacuumOutputInverted = VacuumInvert_checkBox.Checked;
        }


        private void VacuumTime_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(VacuumTime_textBox.Text.Replace(',', '.'), out val))
            {
                VacuumTime_textBox.ForeColor = Color.Black;
                Setting.General_PickupVacuumTime = val;
            }
            else
            {
                VacuumTime_textBox.ForeColor = Color.Red;
            }

        }


        private void VacuumRelease_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(VacuumRelease_textBox.Text.Replace(',', '.'), out val))
            {
                VacuumRelease_textBox.ForeColor = Color.Black;
                Setting.General_PickupReleaseTime = val;
            }
            else
            {
                VacuumRelease_textBox.ForeColor = Color.Red;
            }

        }


        private void SlackCompensation_checkBox_Click(object sender, EventArgs e)
        {
            if (SlackCompensation_checkBox.Checked)
            {
                Cnc.SlackCompensation = true;
                Setting.CNC_SlackCompensation = true;
            }
            else
            {
                Cnc.SlackCompensation = false;
                Setting.CNC_SlackCompensation = false;
            }
        }

        // ==========================================================================================================
        /*
        Probing and probing setup
        */
        // ==========================================================================================================

        private bool Check_HeightCalibrationDone_m()
        {
            if (!Setting.General_HeightCalibrationDone)
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Nozzle height setup not done.",
                    "Calibration not done.", MessageBoxButtons.OK);
                return false;
            }
            return true;
        }

        private bool Nozzle_ProbeDown_m(double depth)
        {
            if (Cnc.ErrorState)
            {
                DisplayText("*** Nozzle_ProbeDown_m(), board in error state.", KnownColor.DarkRed, true);
                return false;
            }

            if (!Check_HeightCalibrationDone_m())
            {
                return false;
            }

            if (Setting.Nozzles_current == 0)
            {
                DisplayText("*** nozzle_ProbeDown, no nozzle loaded.", KnownColor.DarkRed, true);
                return false;
            }

            DisplayText("Probing Z: ");
            DisplayText("Probing Z, touch difference= " + Setting.General_ZTouchDifference.ToString("0.000", CultureInfo.InvariantCulture)
                // + ", switch clearance= " + Setting.CNC_ZswitchClearance.ToString("0.000", CultureInfo.InvariantCulture)
                + ", depth= " + depth.ToString("0.000", CultureInfo.InvariantCulture));
            Cnc.Homing = true;
            if (!Cnc.Nozzle_ProbeDown(Setting.General_ZTouchDifference - depth))
            {
                DisplayText("*** Nozzle probing failed at CNC level.", KnownColor.DarkRed, true);
                Cnc.Homing = false;
                return false;
            }
            Cnc.Homing = false;
            if (Cnc.CurrentZ <0)
            {
                // A user reported negative probing result. I can't see how, but added a check anyway -issue #113 
                DisplayText("*** Probing gave negative result: " + Cnc.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture), KnownColor.DarkRed, true);
                return false;
            }
            DisplayText("Probing result: " + Cnc.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture));
            return true;
        }

        // ==========================================================================
        // setup

        static double Zprevious = 0.0;
        // Z0 to PCB box: Setting.General_Z0toPCB
        // Difference to just touching box: Setting.General_ZTouchDifference

        private void TestSwitchClearance_button_Click(object sender, EventArgs e)
        {
            switch (Setting.NozzleHeightSetupStage)
            {
                case 0:
                    NozzleHeightInstructions_label.Visible = true;
                    NozzleHeightInstructions_label.Text = "Ensure nozzle is over a regular height PCB," +
                                                                 "\n\rthen click \"Next\".";
                    TestSwitchClearanceCancel_button.Visible = true;
                    TestSwitchClearance_button.Text = "Next";
                    Setting.NozzleHeightSetupStage = 1;
                    break;


                case 1:
                    Cnc.Nozzle_ProbeDown(0);
                    DisplayText("Z at bottom: " + Cnc.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture));
                    NozzleHeightInstructions_label.Text = "Jog nozzle up so that it just touches the PCB.\n\r" +
                        "If the nozzle doesn't touch the PCB, click cancel, adjust the switch and start again.\n\r" +
                                "Click \"Next\".";
                    TestSwitchClearance_button.Text = "Next";
                    Setting.NozzleHeightSetupStage = 2;
                    Zprevious = Cnc.CurrentZ;
                    break;

                case 2:
                    Setting.General_ZTouchDifference = Zprevious - Cnc.CurrentZ;
                    TouchDifference_textBox.Text = Setting.General_ZTouchDifference.ToString("0.00", CultureInfo.InvariantCulture);
                    Setting.General_Z0toPCB = Cnc.CurrentZ;
                    Z0toPCB_textBox.Text = Setting.General_Z0toPCB.ToString("0.00", CultureInfo.InvariantCulture);
                    DisplayText("PCB: " + Cnc.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture));
                    DisplayText("touch difference: " + Setting.General_ZTouchDifference.ToString("0.000", CultureInfo.InvariantCulture));
                    CNC_Z_m(0);
                    NozzleHeightInstructions_label.Text = "";
                    TestSwitchClearance_button.Text = "Start";
                    Setting.NozzleHeightSetupStage = 0;
                    Setting.General_HeightCalibrationDone = true;
                    TestSwitchClearanceCancel_button.Visible = false;
                    NozzleHeightInstructions_label.Visible = false;
                    break;
            }

        }

        private void Cancel_TestSwitchClearanceOp()
        {
            Setting.NozzleHeightSetupStage = 0;
            Setting.General_HeightCalibrationDone = false;
            TestSwitchClearance_button.Text = "Start";

            TestSwitchClearanceCancel_button.Visible = false;
            SetProbing_button.Enabled = false;
            CancelProbing_button.Visible = false;
            NozzleHeightInstructions_label.Text = "";
            NozzleHeightInstructions_label.Visible = false;
            ZGuardOn();
            Cnc.Z(0);
        }

        private void TestSwitchClearanceCancel_button_Click(object sender, EventArgs e)
        {
            Cancel_TestSwitchClearanceOp();
        }

        // As pickup/placement Z values are now the "just touching" value, there isn't much need to reset Z values
        /*
        private void OfferTapeZzeroing()
        {
            // If any of the tapes have z heights set, 
            bool ZisSet = false;
            foreach (DataGridViewRow Row in Tapes_dataGridView.Rows)
            {
                if (Row.Cells["Z_Pickup_Column"].Value.ToString() != "--")
                {
                    ZisSet = true;
                    break;
                }
                if (Row.Cells["Z_Place_Column"].Value.ToString() != "--")
                {
                    ZisSet = true;
                    break;
                }
            }
            // ... offer to zero out those
            if (ZisSet)
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Height calibration succesful. Reset pickup and placement heights on tapes?",
                    "Reset Z's?",
                    MessageBoxButtons.OKCancel);
                if (dialogResult == DialogResult.Cancel)
                {
                    return;
                }
                foreach (DataGridViewRow Row in Tapes_dataGridView.Rows)
                {
                    Row.Cells["Z_Pickup_Column"].Value = "--";
                    Row.Cells["Z_Place_Column"].Value = "--";
                }
            }
        }

*/

         // ==========================================================================================================
        // OLD Probing functions

        /*  button clicks reset! Reassing if you get back to these.

     private void CancelProbing()
     {
         SetProbing_button.Text = "Start";
         SetProbing_button.Enabled = false;
         Setting.SetProbing_stage = 0;
         ZGuardOn();
         CancelProbing_button.Visible = false;
         Cnc.ProbingMode(false);
         Cnc.Home_m("Z");
         SetProbing_button.Enabled = true;
     }

     private void CancelProbing_button_Click(object sender, EventArgs e)
     {
         // CancelProbing();
     }

     private void SetProbing_button_Click(object sender, EventArgs e)
     {
         ZGuardOff();
         switch (Setting.SetProbing_stage)
         {
             case 0:
                 CancelProbing_button.Visible = true;
                 CancelProbing_button.Enabled = true;
                 NozzleHeightInstructions_label.Text = "Put a regular height PCB under the Nozzle, \n\rthen click \"Next\"";
                 SetProbing_button.Text = "Next";
                 Setting.SetProbing_stage = 1;
                 break;

             case 1:
                 SetProbing_button.Enabled = false;
                 CancelProbing_button.Enabled = false;
                 if (!Nozzle_ProbeDown_m())
                 {
                     CancelProbing();
                     return;
                 }
                 SetProbing_button.Enabled = true;
                 CancelProbing_button.Enabled = true;
                 Setting.General_Z0toPCB = Cnc.CurrentZ;
                 NozzleHeightInstructions_label.Text = "Jog Z axis until the Nozzle just barely touches the PCB\nThen click \"Next\"";
                 Setting.SetProbing_stage = 2;
                 break;

             case 2:
                 Setting.General_ZTouchDifference = Setting.General_Z0toPCB - Cnc.CurrentZ;
                 Setting.General_Z0toPCB = Cnc.CurrentZ;
                 Cnc.ProbingMode(false);
                 SetProbing_button.Text = "Start";
                 NozzleHeightInstructions_label.Text = "";
                 CancelProbing_button.Visible = false;
                 SetProbing_button.Enabled = false;
                 Cnc.Home_m("Z");
                 ZGuardOn();
                 SetProbing_button.Enabled = true;
                 ShowMessageBox(
                    "Probing Backoff set successfully.\n" +
                         "PCB surface: " + Setting.General_Z0toPCB.ToString("0.00", CultureInfo.InvariantCulture) +
                         "\nDifference:  " + Setting.General_ZTouchDifference.ToString("0.00", CultureInfo.InvariantCulture),
                    "Done",
                    MessageBoxButtons.OK);
                 Setting.SetProbing_stage = 0;
                 Z0toPCB_textBox.Text = Setting.General_Z0toPCB.ToString("0.00", CultureInfo.InvariantCulture);
                 TouchDifference_textBox.Text = Setting.General_ZTouchDifference.ToString("0.00", CultureInfo.InvariantCulture);

                 // If tapes have z heights set, offer to zero out those:
                 bool ZisSet = false;
                 foreach (DataGridViewRow Row in Tapes_dataGridView.Rows)
                 {
                     if (Row.Cells["Z_Pickup_Column"].Value.ToString() != "--")
                     {
                         ZisSet = true;
                         break;
                     }
                     if (Row.Cells["Z_Place_Column"].Value.ToString() != "--")
                     {
                         ZisSet = true;
                         break;
                     }
                 }
                 if (ZisSet)
                 {
                     DialogResult dialogResult = ShowMessageBox(
                         "Reset pickup and placement heights on tapes?",
                         "Reset Z's?",
                         MessageBoxButtons.OKCancel);
                     if (dialogResult == DialogResult.Cancel)
                     {
                         break;
                     }
                     foreach (DataGridViewRow Row in Tapes_dataGridView.Rows)
                     {
                         Row.Cells["Z_Pickup_Column"].Value = "--";
                         Row.Cells["Z_Place_Column"].Value = "--";
                     }
                 }

                 break;
         }
     }
         */

        // =================================================================================
        #region Bookmarks

        private void SetMark1_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            Setting.General_Mark1X = Cnc.CurrentX;
            Setting.General_Mark1Y = Cnc.CurrentY;
            Setting.General_Mark1A = Cnc.CurrentA;
            Setting.General_Mark1Name = Mark1_textBox.Text;
            Bookmark1_button.Text = Setting.General_Mark1Name;
            DisplayText("Mark 1 \" " + Setting.General_Mark1Name + 
                "\" set to X: " + Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture) +
                ", Y: " + Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture) +
                ", A: " + Cnc.CurrentA.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void SetMark2_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            Setting.General_Mark2X = Cnc.CurrentX;
            Setting.General_Mark2Y = Cnc.CurrentY;
            Setting.General_Mark2A = Cnc.CurrentA;
            Setting.General_Mark2Name = Mark2_textBox.Text;
            Bookmark2_button.Text = Setting.General_Mark2Name;
            DisplayText("Mark 2 \" " + Setting.General_Mark2Name +
                "\" set to X: " + Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture) +
                ", Y: " + Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture) +
                ", A: " + Cnc.CurrentA.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void SetMark3_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            Setting.General_Mark3X = Cnc.CurrentX;
            Setting.General_Mark3Y = Cnc.CurrentY;
            Setting.General_Mark3A = Cnc.CurrentA;
            Setting.General_Mark3Name = Mark3_textBox.Text;
            Bookmark3_button.Text = Setting.General_Mark3Name;
            DisplayText("Mark 5 \" " + Setting.General_Mark5Name +
                "\" set to X: " + Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture) +
                ", Y: " + Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture) +
                ", A: " + Cnc.CurrentA.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void SetMark4_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            Setting.General_Mark4X = Cnc.CurrentX;
            Setting.General_Mark4Y = Cnc.CurrentY;
            Setting.General_Mark4A = Cnc.CurrentA;
            Setting.General_Mark4Name = Mark4_textBox.Text;
            Bookmark4_button.Text = Setting.General_Mark4Name;
            DisplayText("Mark 4 \" " + Setting.General_Mark4Name +
                "\" set to X: " + Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture) +
                ", Y: " + Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture) +
                ", A: " + Cnc.CurrentA.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void SetMark5_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            Setting.General_Mark5X = Cnc.CurrentX;
            Setting.General_Mark5Y = Cnc.CurrentY;
            Setting.General_Mark5A = Cnc.CurrentA;
            Setting.General_Mark5Name = Mark5_textBox.Text;
            Bookmark5_button.Text = Setting.General_Mark5Name;
            DisplayText("Mark 5 \" " + Setting.General_Mark5Name +
                "\" set to X: " + Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture) +
                ", Y: " + Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture) +
                ", A: " + Cnc.CurrentA.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void SetMark6_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            Setting.General_Mark6X = Cnc.CurrentX;
            Setting.General_Mark6Y = Cnc.CurrentY;
            Setting.General_Mark6A = Cnc.CurrentA;
            Setting.General_Mark6Name = Mark6_textBox.Text;
            Bookmark6_button.Text = Setting.General_Mark6Name;
            DisplayText("Mark 6 \" " + Setting.General_Mark6Name +
                "\" set to X: " + Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture) +
                ", Y: " + Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture) +
                ", A: " + Cnc.CurrentA.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void Bookmark1_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (MovementIsBusy_m())
            {
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark1X = Cnc.CurrentX;
                Setting.General_Mark1Y = Cnc.CurrentY;
                Setting.General_Mark1A = Cnc.CurrentA;
                return;
            };
            CNC_XYA_m(Setting.General_Mark1X, Setting.General_Mark1Y, Setting.General_Mark1A);
        }

        private void Bookmark2_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (MovementIsBusy_m())
            {
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark2X = Cnc.CurrentX;
                Setting.General_Mark2Y = Cnc.CurrentY;
                Setting.General_Mark2A = Cnc.CurrentA;
                return;
            };
            CNC_XYA_m(Setting.General_Mark2X, Setting.General_Mark2Y, Setting.General_Mark2A);
        }

        private void Bookmark3_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (MovementIsBusy_m())
            {
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark3X = Cnc.CurrentX;
                Setting.General_Mark3Y = Cnc.CurrentY;
                Setting.General_Mark3A = Cnc.CurrentA;
                return;
            };
            CNC_XYA_m(Setting.General_Mark3X, Setting.General_Mark3Y, Setting.General_Mark3A);
        }

        private void Bookmark4_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (MovementIsBusy_m())
            {
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark4X = Cnc.CurrentX;
                Setting.General_Mark4Y = Cnc.CurrentY;
                Setting.General_Mark4A = Cnc.CurrentA;
                return;
            };
            CNC_XYA_m(Setting.General_Mark4X, Setting.General_Mark4Y, Setting.General_Mark4A);
        }

        private void Bookmark5_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (MovementIsBusy_m())
            {
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark5X = Cnc.CurrentX;
                Setting.General_Mark5Y = Cnc.CurrentY;
                Setting.General_Mark5A = Cnc.CurrentA;
                return;
            };
            CNC_XYA_m(Setting.General_Mark5X, Setting.General_Mark5Y, Setting.General_Mark5A);
        }

        private void Bookmark6_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (MovementIsBusy_m())
            {
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark6X = Cnc.CurrentX;
                Setting.General_Mark6Y = Cnc.CurrentY;
                Setting.General_Mark6A = Cnc.CurrentA;
                return;
            };
            CNC_XYA_m(Setting.General_Mark6X, Setting.General_Mark6Y, Setting.General_Mark6A);
        }
        #endregion


        // =================================================================================
        private void SmallMovement_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_SmallMovementSpeed = SmallMovement_numericUpDown.Value;
            Cnc.SmallMovementSpeed = SmallMovement_numericUpDown.Value;
        }

        
        private void SquareCorrection_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(SquareCorrection_textBox.Text.Replace(',', '.'), out val))
            {
                SquareCorrection_textBox.ForeColor = Color.Black;
                Setting.CNC_SquareCorrection = val;
                CNC.SquareCorrection = val;
            }
            else
            {
                SquareCorrection_textBox.ForeColor = Color.Red;
            }

        }

        private void CtlrJogSpeed_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_CtlrJogSpeed = (int)CtlrJogSpeed_numericUpDown.Value;

        }

        private void NormalJogSpeed_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_NormalJogSpeed = (int)NormalJogSpeed_numericUpDown.Value;
        }

        private void AltJogSpeed_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_AltJogSpeed = (int)AltJogSpeed_numericUpDown.Value;
        }

        private void DisableLog_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DisableLog_checkBox.Checked)
            {
                DisplayText("** Logging disabled **", KnownColor.Black, true);
            }
        }

        private void SlackCompensationA_checkBox_Click(object sender, EventArgs e)
        {
            if (SlackCompensationA_checkBox.Checked)
            {
                Cnc.SlackCompensationA = true;
                Setting.CNC_SlackCompensationA = true;
            }
            else
            {
                Cnc.SlackCompensationA = false;
                Setting.CNC_SlackCompensationA = false;
            }
        }


        private void Z0toPCB_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(Z0toPCB_textBox.Text.Replace(',', '.'), out val))
            {
                Z0toPCB_textBox.ForeColor = Color.Black;
                Setting.General_Z0toPCB = val;
            }
            else
            {
                Z0toPCB_textBox.ForeColor = Color.Red;
            }

        }


        private void PickupDepth_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(PickupDepth_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Placement_Pickup_Depth = val;
                PickupDepth_textBox.ForeColor = Color.Black;
            }
            else
            {
                PickupDepth_textBox.ForeColor = Color.Red;
            }
        }


        private void PlacementDepth_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(PlacementDepth_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Placement_Placement_Depth = val;
                PlacementDepth_textBox.ForeColor = Color.Black;
            }
            else
            {
                PlacementDepth_textBox.ForeColor = Color.Red;
            }
        }


        private void NozzleBelowPCB_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleBelowPCB_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_BelowPCB_Allowance = val;
            }
        }


        private void TouchDifference_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(TouchDifference_textBox.Text.Replace(',', '.'), out val))
            {
                TouchDifference_textBox.ForeColor = Color.Black;
                Setting.General_ZTouchDifference = val;
            }
            else
            {
                TouchDifference_textBox.ForeColor = Color.Red;
            }

        }

        #endregion Basic setup page functions

        // =================================================================================
        // Run job page functions
        // =================================================================================
        #region Job page functions

        private class PhysicalComponent
        {
            public string Designator { get; set; }
            // public string Footprint { get; set; }
            public double X_nominal { get; set; }
            public double Y_nominal { get; set; }
            // public double Rotation { get; set; }
            public double X_machine { get; set; }
            public double Y_machine { get; set; }
            // public string Method { get; set; }
            // public string MethodParameter { get; set; }
        }

        private void RunJob_tabPage_Begin()
        {
            DisplayText("Run Job tab begin");
            SetDownCameraDefaults();
            SelectCamera(DownCamera);
            if (!DownCamera.IsRunning())
            {
                ShowMessageBox(
                    "Problem starting down camera. Please fix before continuing.",
                    "Down Camera problem",
                    MessageBoxButtons.OK
                );
            }
            DownCamera.DrawCross = true;
            DownCamera.BoxSizeX = 200;
            DownCamera.BoxSizeY = 200;
            DownCamera.BoxRotationDeg = 0;
            DownCamera.TestAlgorithm = false;
            DownCamera.Draw_Snapshot = false;
            DownCamera.FindCircles = false;
            DownCamera.DrawDashedCross = false;
        }

        private void RunJob_tabPage_End()
        {
        }

        // =================================================================================
        private void ResetPlacedDataToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // the foreach loop below ignores last checked box, unless we manually select some other cell before. ??
            CadData_GridView.CurrentCell = CadData_GridView[0, 0];
            CadDataDelay_label.Text = "Resetting...";
            CadDataDelay_label.Visible = true;
            this.Refresh();
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                Row.Cells["CADdataPlacedColumn"].Value = false;
            }
            CadData_GridView.ClearSelection();  // and we don't want a random celected cell
            CadDataDelay_label.Visible = false;
            Update_GridView(CadData_GridView);
            this.Refresh();
        }


        // =================================================================================
        // CAD data and Job data load and save functions
        // =================================================================================
        private string CadDataFileName = "";
        private string JobFileName = "";

        private bool LoadCadData_m()
        {
            String[] AllLines;

            // read in CAD data (.csv file)
            if (CAD_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    bool result;
                    CadDataFileName = CAD_openFileDialog.FileName;
                    CadFileName_label.Text = System.IO.Path.GetFileName(CadDataFileName);
                    CadFilePath_label.Text = System.IO.Path.GetDirectoryName(CadDataFileName);
                    AllLines = File.ReadAllLines(CadDataFileName, Encoding.Default);
                    if (System.IO.Path.GetExtension(CAD_openFileDialog.FileName) == ".pos")
                    {
                        result = ParseKiCadData_m(AllLines);
                    }
                    else
                    {
                        result = ParseCadData_m(AllLines, false);
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    Cursor.Current = Cursors.Default;
                    ShowMessageBox(
                        "Error in file, Msg: " + ex.Message,
                        "Can't read CAD file",
                        MessageBoxButtons.OK);
                    CadData_GridView.Rows.Clear();
                    CadFileName_label.Text = "--";
                    CadFilePath_label.Text = "--";
                    CadDataFileName = "--";
                    CadDataDelay_label.Visible = false;
                    Update_GridView(CadData_GridView);
                    this.Refresh();
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        // =================================================================================
        private bool LoadJobData_m(string JobFileName)
        {
            String[] AllLines;
            try
            {
                AllLines = File.ReadAllLines(JobFileName);
                JobFileName_label.Text = System.IO.Path.GetFileName(JobFileName);
                JobFilePath_label.Text = System.IO.Path.GetDirectoryName(JobFileName);
                if (!ParseJobData(AllLines))
                {
                    JobData_GridView.Rows.Clear();
                    JobFileName_label.Text = "--";
                    JobFilePath_label.Text = "--";
                    CadDataFileName = "--";
                    return false;
                }
            }
            catch (Exception ex)
            {
                ShowMessageBox(
                    "Error in file, Msg: " + ex.Message,
                    "Can't read job file (" + JobFileName + ")",
                    MessageBoxButtons.OK);
                JobData_GridView.Rows.Clear();
                JobFileName_label.Text = "--";
                JobFilePath_label.Text = "--";
                CadDataFileName = "--";
                return false;
            };
            return true;
        }

        // =================================================================================
        private void LoadCadData_button_Click(object sender, EventArgs e)
        {
            ValidMeasurement_checkBox.Checked = false;
            if (LoadCadData_m())
            {
                SaveTempCADdata();
                // Read in job data (.lpj file), if exists
                string ext = System.IO.Path.GetExtension(CadDataFileName);
                JobFileName = CadDataFileName.Replace(ext, ".lpj");
                if (File.Exists(JobFileName))
                {
                    if (!LoadJobData_m(JobFileName))
                    {
                        ShowMessageBox(
                            "Attempt to read in existing Job Data file failed. Job data automatically created, review situation!",
                            "Job Data load error",
                            MessageBoxButtons.OK);
                        FillJobData_GridView();
                    }
                }
                else
                {
                    // If not, build job data ourselves.
                    FillJobData_GridView();
                    FillDefaultJobValues();
                    JobFileName_label.Text = "--";
                    JobFilePath_label.Text = "--";
                }
            }
            else
            {
                // CAD data load failed, clear to false data
                CadData_GridView.Rows.Clear();
                CadFileName_label.Text = "--";
                CadFilePath_label.Text = "--";
                CadDataFileName = "--";
            }
            MakeCADdataClean();
        }

        // =================================================================================
        private void SaveCadData_button_Click(object sender, EventArgs e)
        {
            if (CadData_GridView.RowCount < 1)
            {
                ShowMessageBox(
                    "No Data",
                    "No Data",
                    MessageBoxButtons.OK
                );
                return;
            }

            Job_saveFileDialog.Filter = "CSV placement files (*.csv)|*.csv|All files (*.*)|*.*";
            Job_saveFileDialog.FileName = CadFileName_label.Text;

            if (Job_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SaveCADdata(Job_saveFileDialog.FileName);
                CadDataFileName = Job_saveFileDialog.FileName;
                CadFileName_label.Text = System.IO.Path.GetFileName(CadDataFileName);
                CadFilePath_label.Text = System.IO.Path.GetDirectoryName(CadDataFileName);
            }
        }

        private bool SaveCADdata(string filename, bool tempfile = false)
        {
            try
            {
                string OutLine;
                using (StreamWriter f = new StreamWriter(filename))
                {
                    if (tempfile)
                    {
                        f.WriteLine(CadFileName_label.Text);
                        f.WriteLine(CadFilePath_label.Text);
                        MakeCADdataDirty();  // it is dirty, since it didn't came from the original file
                    }
                    else 
                    {
                        MakeCADdataClean();
                    }
                    // Write header
                    OutLine = "\"Component\",\"Value\",\"Footprint\",\"Placed\",\"X\",\"Y\",\"Rotation\"";
                    f.WriteLine(OutLine);
                    // write data
                    foreach (DataGridViewRow Row in CadData_GridView.Rows)
                    {
                        OutLine = "\"" + Row.Cells["CADdataComponentColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + Row.Cells["CADdataValueColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + Row.Cells["CADdataFootprintColumn"].Value.ToString() + "\"";
                        if (Row.Cells["CADdataPlacedColumn"].Value == null)
                        {
                            OutLine += ",\"false\"";
                        }
                        else
                        {
                            OutLine += ",\"" + Row.Cells["CADdataPlacedColumn"].Value.ToString() + "\"";
                        }
                        OutLine += ",\"" + Row.Cells["CADdataXnominalColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + Row.Cells["CADdataYnominalColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + Row.Cells["CADdataRotationNominalColumn"].Value.ToString() + "\"";
                        f.WriteLine(OutLine);
                    }
                }
                return true;
            }
            catch (System.Exception excep)
            {

                DisplayText("SaveCADdata failed: "+ excep.Message);
                return false;
            }
        }


        // =================================================================================
        private void LoadTempJobData()
        {
            String[] AllLines;
            string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = FileName.LastIndexOf('\\');
            FileName = FileName.Remove(i + 1);
            FileName = FileName + "JobDataSave.csv";
            if (!File.Exists(FileName))
            {
                DisplayText("No saved temp job data file");
                MakeJobDataClean();
                return;
            }
            else
            {
                MakeJobDataDirty();
            }
            try
            {
                DisplayText("Loading temp job data file");
                AllLines = File.ReadAllLines(FileName);
                JobFileName_label.Text = AllLines[0];
                JobFilePath_label.Text = AllLines[1];
                AllLines = AllLines.Skip(2).ToArray();
                if (!ParseJobData(AllLines))
                {
                    JobData_GridView.Rows.Clear();
                    JobFileName_label.Text = "--";
                    JobFilePath_label.Text = "--";
                    CadDataFileName = "--";
                }
                return;
            }
            catch (Exception ex)
            {
                DisplayText("Could not read temp job data file", KnownColor.DarkRed, true);
                DisplayText("Msg: " + ex.Message, KnownColor.DarkRed, true);
                JobData_GridView.Rows.Clear();
                return;
            }
        }

        private void LoadTempCADdata()
        {
            String[] AllLines;
            string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = FileName.LastIndexOf('\\');
            FileName = FileName.Remove(i + 1);
            FileName = FileName + "CadDataSave.csv";
            if (!File.Exists(FileName))
            {
                DisplayText("No saved temp CAD data file");
                MakeCADdataClean();
                return;
            }
            else
            {
                MakeCADdataDirty();
            }
            try
            {
                DisplayText("Loading temp CAD data file");
                AllLines = File.ReadAllLines(FileName);
                CadFileName_label.Text = AllLines[0];
                CadFilePath_label.Text = AllLines[1];
                AllLines = AllLines.Skip(2).ToArray();
                ParseCadData_m(AllLines, false);
                return;
            }
            catch (Exception ex)
            {
                DisplayText("Could not read temp CAD data file", KnownColor.DarkRed, true);
                DisplayText("Msg: " + ex.Message, KnownColor.DarkRed, true);
                CadData_GridView.Rows.Clear();
                return;
            }
        }

        // =================================================================================
        private bool SaveTempCADdata()
        {
            DisplayText("Saving temp CAD data file");
            if ( CadFileName_label.Text!="----")
            {
                string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                int i = FileName.LastIndexOf('\\');
                FileName = FileName.Remove(i + 1);
                FileName = FileName + "CadDataSave.csv";
                return SaveCADdata(FileName, true);
            }
            return true;
        }

        private bool SaveTempJobData()
        {
            DisplayText("Saving temp job data file");
            if (CadFileName_label.Text != "----")
            {
                string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                int i = FileName.LastIndexOf('\\');
                FileName = FileName.Remove(i + 1);
                FileName = FileName + "JobDataSave.csv";
                return SaveJobData(FileName, true);
            }
            return true;
        }

        // =================================================================================
        private void MakeCADdataDirty()
        {
            CAD_label.Text = "FileName*:";
        }

        private void MakeCADdataClean()
        {
            CAD_label.Text = "FileName:";
        }

        // =================================================================================
        private void MakeJobDataDirty()
        {
            Job_label.Text = "FileName*:";
        }

        private void MakeJobDataClean()
        {
            Job_label.Text = "FileName:";
        }


        // =================================================================================
        private void CadData_GridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (CadData_GridView.CurrentCell.OwningColumn.Name  == "CADdataPlacedColumn")
            {
                MakeCADdataDirty(); 

            }
        }

        private void CadData_GridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // Machine coordinates can be manually edited (but I don't know why you would want to do that),
            // but they are not saved, therefore the edit of those does not make data dirty, but edit of other cells do
            if ((CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataComponentColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataValueColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataFootprintColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataPlacedColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataXNominalColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataYNominalColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataRotationNomColumn")
                )
            {
                MakeCADdataDirty();
            }
            // Invalidate measurements if user manually edits placement info - issue #107
            if ((CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataXNominalColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataYNominalColumn") ||
                (CadData_GridView.CurrentCell.OwningColumn.Name == "CADdataRotationNomColumn")
                )
            {
                ValidMeasurement_checkBox.Checked = false;
            }


        }



        // =================================================================================
        private void JobDataLoad_button_Click(object sender, EventArgs e)
        {
            if (Job_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                JobFileName = Job_openFileDialog.FileName;
                LoadJobData_m(JobFileName);
            }
        }

        // =================================================================================
        private void JobDataSave_button_Click(object sender, EventArgs e)
        {
            if (JobData_GridView.RowCount < 1)
            {
                ShowMessageBox(
                    "No Data",
                    "Empty Job",
                    MessageBoxButtons.OK
                );
                return;
            }

            Job_saveFileDialog.Filter = "LitePlacer Job files (*.lpj)|*.lpj|All files (*.*)|*.*";
            if (JobFileName_label.Text.StartsWith("--"))
            {
                Job_saveFileDialog.FileName = "";
            }
            else
            {
                Job_saveFileDialog.FileName = JobFileName_label.Text;
            }

            if (Job_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SaveJobData(Job_saveFileDialog.FileName);
                JobFileName = Job_saveFileDialog.FileName;
                JobFileName_label.Text = System.IO.Path.GetFileName(JobFileName);
                JobFilePath_label.Text = System.IO.Path.GetDirectoryName(JobFileName);
            }
        }

        private const string JobdataVersion = "2022";

        private bool SaveJobData(string filename, bool tempfile = false)
        {
            try
            {
                string OutLine;
                using (StreamWriter f = new StreamWriter(filename))
                {
                    if (tempfile)
                    {
                        f.WriteLine(JobFileName_label.Text);
                        f.WriteLine(JobFilePath_label.Text);
                        MakeJobDataDirty();  // it is dirty, since it didn't came from the original file
                    }
                    else
                    {
                        MakeJobDataClean();
                    }
                    f.WriteLine(JobdataVersion);  // used in ParseJobData()

                    // for each row in job datagrid,
                    for (int i = 0; i < JobData_GridView.RowCount; i++)
                    {
                        OutLine = "\"" + JobData_GridView.Rows[i].Cells["JobdataCountColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["JobDataValueColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["JobDataFootprintColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["JobdataMethodColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["JobdataMethodParametersColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["PlacementMethodColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["PlacementParameterColumn"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["JobdataComponentsColumn"].Value.ToString() + "\"";
                        if (JobData_GridView.Rows[i].Cells["JobDataNozzleColumn"].Value != null)
                        {
                            OutLine += ",\"" + JobData_GridView.Rows[i].Cells["JobDataNozzleColumn"].Value.ToString() + "\"";
                        }
                        else
                        {
                            OutLine += ",\"" + "\"";
                        }
                        f.WriteLine(OutLine);
                    }
                    return true;
                }
            }
            catch (System.Exception excep)
            {

                DisplayText("SaveJobData failed: " + excep.Message);
                return false;
            }
        }


        // =================================================================================
        private bool ParseJobData(String[] AllLines)
        {
            JobData_GridView.Rows.Clear();
            string WarningMessage = "";
            bool quit = false;
            if (AllLines[0] == "2022")
            {
                // format is ComponentCount, value, footprint, (pickup)Method, parameters, placement method, placement parameters, ComponentList, nozzle
                for (int i = 1; i < AllLines.Length; i++)
                {
                    List<String> Line = SplitCSV(AllLines[i], ',', out quit);
                    if (quit)
                    {
                        return false;
                    }
                    JobData_GridView.Rows.Add();
                    int Last = JobData_GridView.RowCount - 1;
                    JobData_GridView.Rows[Last].Cells["JobdataCountColumn"].Value = Line[0];
                    JobData_GridView.Rows[Last].Cells["JobDataValueColumn"].Value = Line[1];
                    JobData_GridView.Rows[Last].Cells["JobDataFootprintColumn"].Value = Line[2];
                    JobData_GridView.Rows[Last].Cells["JobdataMethodColumn"].Value = Line[3];
                    JobData_GridView.Rows[Last].Cells["JobdataMethodParametersColumn"].Value = Line[4];
                    JobData_GridView.Rows[Last].Cells["PlacementMethodColumn"].Value = Line[5];
                    JobData_GridView.Rows[Last].Cells["JobdataComponentsColumn"].Value = Line[7];
                    JobData_GridView.Rows[Last].Cells["JobDataNozzleColumn"].Value = Line[8];
                    JobData_GridView.Rows[Last].Cells["PlacementParameterColumn"].Value = Line[6];
                }
            }
            else if (AllLines[0] == "2020")
            {
                // format is ComponentCount value footprint GroupMethod MethodParamAllComponents ComponentList nozzle
                for (int i = 1; i < AllLines.Length; i++)
                {
                    List<String> Line = SplitCSV(AllLines[i], ',', out quit);
                    if (quit)
                    {
                        return false;
                    }
                    JobData_GridView.Rows.Add();
                    int Last = JobData_GridView.RowCount - 1;
                    JobData_GridView.Rows[Last].Cells["JobdataCountColumn"].Value = Line[0];
                    JobData_GridView.Rows[Last].Cells["JobDataValueColumn"].Value = Line[1];
                    JobData_GridView.Rows[Last].Cells["JobDataFootprintColumn"].Value = Line[2];
                    JobData_GridView.Rows[Last].Cells["JobdataMethodColumn"].Value = Line[3];
                    JobData_GridView.Rows[Last].Cells["JobdataMethodParametersColumn"].Value = Line[4];
                    JobData_GridView.Rows[Last].Cells["JobdataComponentsColumn"].Value = Line[5];
                    JobData_GridView.Rows[Last].Cells["JobDataNozzleColumn"].Value = Line[6];
                    JobData_GridView.Rows[Last].Cells["PlacementMethodColumn"].Value = "--";
                    JobData_GridView.Rows[Last].Cells["PlacementParameterColumn"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
                }
            }
            else
            {
                // format is ComponentCount ComponentType GroupMethod MethodParamAllComponents ComponentList nozzle
                int LineNo = 1;
                foreach (string LineIn in AllLines)
                {
                    List<String> Line = SplitCSV(LineIn, ',', out quit);
                    if (quit)
                    {
                        return false;
                    }
                    JobData_GridView.Rows.Add();
                    int Last = JobData_GridView.RowCount - 1;
                    JobData_GridView.Rows[Last].Cells["JobdataCountColumn"].Value = Line[0];
                    string ComponentType = Line[1];
                    if (ComponentType.Contains(" | "))
                    {
                        int i = ComponentType.IndexOf('|');
                        JobData_GridView.Rows[Last].Cells["JobDataValueColumn"].Value = ComponentType.Substring(0, i - 2);
                        JobData_GridView.Rows[Last].Cells["JobDataFootprintColumn"].Value = 
                            ComponentType.Substring(i + 3, ComponentType.Length - i - 3);

                    }
                    JobData_GridView.Rows[Last].Cells["JobdataMethodColumn"].Value = Line[2];
                    if (Line[2] == "Fiducials")
                    {
                        // Does the saved algorithm stil exist?
                        string AlgName = Line[3];
                        if (VideoAlgorithms.AlgorithmExists(AlgName))
                        {
                            JobData_GridView.Rows[Last].Cells["JobdataMethodParametersColumn"].Value = AlgName;
                        }
                        else
                        {
                            WarningMessage = WarningMessage + "Algorithm " + AlgName + " used on job data file " +
                                "as a parameter for method " + Line[2] + " does not exist.\n\r";
                            JobData_GridView.Rows[Last].Cells["JobdataMethodParametersColumn"].Value = "--";
                        }
                    }
                    else
                    {
                        JobData_GridView.Rows[Last].Cells["JobdataMethodParametersColumn"].Value = Line[3];
                    }
                    JobData_GridView.Rows[Last].Cells["JobdataComponentsColumn"].Value = Line[4];
                    if (Line.Count>5)
                    {
                        JobData_GridView.Rows[Last].Cells["JobDataNozzleColumn"].Value = Line[5];
                    }
                    JobData_GridView.Rows[Last].Cells["PlacementMethodColumn"].Value = "--";
                    JobData_GridView.Rows[Last].Cells["PlacementParameterColumn"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
                    LineNo++;
                }
            }

            JobData_GridView.ClearSelection();
            Update_GridView(JobData_GridView);
            return true;
        }

        // =================================================================================
        // JobData_GridView and CadData_GridView handling
        // JobData_GridView has the components of same type grouped to one component type per line.
        // Public, as it is called from panelize process, too.
        // =================================================================================
        public void FillJobData_GridView()
        {
            string CurrentComponentValue = "";
            string CurrentComponentFootprint = "";
            int ComponentCount = 0;
            JobData_GridView.Rows.Clear();
            int TypeRow;

            foreach (DataGridViewRow InRow in CadData_GridView.Rows)
            {
                CurrentComponentValue = InRow.Cells["CADdataValueColumn"].Value.ToString();
                CurrentComponentFootprint = InRow.Cells["CADdataFootprintColumn"].Value.ToString();
                TypeRow = -1;
                // Have we seen this component type already?
                foreach (DataGridViewRow JobRow in JobData_GridView.Rows)
                {
                    if ((JobRow.Cells["JobDataValueColumn"].Value.ToString() == CurrentComponentValue) &&
                        (JobRow.Cells["JobDataFootprintColumn"].Value.ToString() == CurrentComponentFootprint))
                    {
                        TypeRow = JobRow.Index;
                        break;
                    }
                }
                if (TypeRow == -1)
                {
                    // No, create a new row
                    JobData_GridView.Rows.Add();
                    DataGridViewRow OutRow = JobData_GridView.Rows[JobData_GridView.RowCount - 1];
                    OutRow.Cells["JobdataCountColumn"].Value = "1";
                    OutRow.Cells["JobDataValueColumn"].Value = CurrentComponentValue;
                    OutRow.Cells["JobDataFootprintColumn"].Value = CurrentComponentFootprint;
                    OutRow.Cells["JobdataMethodColumn"].Value = "?";
                    OutRow.Cells["JobdataMethodParametersColumn"].Value = "--";
                    OutRow.Cells["JobdataComponentsColumn"].Value = InRow.Cells["CADdataComponentColumn"].Value.ToString();
                    OutRow.Cells["PlacementMethodColumn"].Value = "--";
                    OutRow.Cells["PlacementParameterColumn"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
                }
                else
                {
                    // Yes, increment component count and add component name to list
                    string tmp = JobData_GridView.Rows[TypeRow].Cells["JobdataCountColumn"].Value.ToString();
                    ComponentCount = Convert.ToInt32(tmp, CultureInfo.InvariantCulture);
                    ComponentCount++;
                    JobData_GridView.Rows[TypeRow].Cells["JobdataCountColumn"].Value = ComponentCount.ToString(CultureInfo.InvariantCulture);
                    // and add component name to list
                    string CurrentComponentList = JobData_GridView.Rows[TypeRow].Cells["JobdataComponentsColumn"].Value.ToString();
                    CurrentComponentList = CurrentComponentList + ',' + InRow.Cells["CADdataComponentColumn"].Value.ToString();
                    JobData_GridView.Rows[TypeRow].Cells["JobdataComponentsColumn"].Value = CurrentComponentList;
                }
            }
        }

        private void FillDefaultJobValues()
        {
            foreach (DataGridViewRow JobRow in JobData_GridView.Rows)
            {
                if (JobRow.Cells["JobdataMethodColumn"].Value.ToString() != "Fiducials")
                {
                    JobRow.Cells["JobdataMethodColumn"].Value = "Place Fast";
                    if (Setting.Nozzles_Enabled)
                    {
                        JobRow.Cells["JobDataNozzleColumn"].Value = Setting.Nozzles_default.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }


        private void Up_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            DataGrid_Up_button(JobData_GridView);
        }

        private void Down_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            DataGrid_Down_button(JobData_GridView);
        }

        private void DeleteComponentGroup_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            foreach (DataGridViewCell oneCell in JobData_GridView.SelectedCells)
            {
                if (oneCell.Selected)
                    JobData_GridView.Rows.RemoveAt(oneCell.RowIndex);
            }
        }

        private DataGridViewRow ClipBoardRow;

        private void CopyRow_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            ClipBoardRow = JobData_GridView.CurrentRow;
        }

        private void PasteRow_button_Click(object sender, EventArgs e)
        {
            if (ClipBoardRow==null)
            {
                return;
            }
            MakeJobDataDirty();
            for (int i = 0; i < JobData_GridView.ColumnCount; i++)
            {
                JobData_GridView.CurrentRow.Cells[i].Value = ClipBoardRow.Cells[i].Value;
            }
        }


        private void NewRow_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            int index = 0;
            if (JobData_GridView.RowCount != 0)
            {
                index = JobData_GridView.CurrentRow.Index;
            }
            JobData_GridView.Rows.Insert(index);
            JobData_GridView.Rows[index].Cells["JobDataValueColumn"].Value = "--";
            JobData_GridView.Rows[index].Cells["JobDataFootprintColumn"].Value = "--";
            JobData_GridView.Rows[index].Cells["JobdataCountColumn"].Value = "--";
            JobData_GridView.Rows[index].Cells["JobdataMethodColumn"].Value = "?";
            JobData_GridView.Rows[index].Cells["JobdataMethodParametersColumn"].Value = "--";
            JobData_GridView.Rows[index].Cells["JobDataNozzleColumn"].Value = Setting.Nozzles_default.ToString();
            JobData_GridView.Rows[index].Cells["JobdataComponentsColumn"].Value = "--";
            JobData_GridView.Rows[index].Cells["PlacementMethodColumn"].Value = "--";
            JobData_GridView.Rows[index].Cells["PlacementParameterColumn"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
        }

        private void AddCadDataRow_button_Click(object sender, EventArgs e)
        {
            int index = 0;
            if (CadData_GridView.RowCount!=0)
            {
                index = CadData_GridView.CurrentRow.Index;
            }
            CadData_GridView.Rows.Insert(index);
            CadData_GridView.Rows[index].Cells["CADdataComponentColumn"].Value = "new_component";
            CadData_GridView.Rows[index].Cells["CADdataValueColumn"].Value = "value";
            CadData_GridView.Rows[index].Cells["CADdataFootprintColumn"].Value = "footprint";
            CadData_GridView.Rows[index].Cells["CADdataXnominalColumn"].Value = "0.0";
            CadData_GridView.Rows[index].Cells["CADdataYnominalColumn"].Value = "0.0";
            CadData_GridView.Rows[index].Cells["CADdataRotationNominalColumn"].Value = "0.0";
            CadData_GridView.CurrentCell = CadData_GridView.Rows[index].Cells[0];
            SaveTempCADdata();  // makes data dirty
        }

        private void RebuildJobData_button_Click(object sender, EventArgs e)
        {
            // TO DO: Error checking here
            FillJobData_GridView();
            JobFileName_label.Text = "--";
            JobFilePath_label.Text = "--";

        }

        private void DeleteCadDataRow_button_Click(object sender, EventArgs e)
        {
            foreach (DataGridViewCell oneCell in CadData_GridView.SelectedCells)
            {
                if (oneCell.Selected)
                    CadData_GridView.Rows.RemoveAt(oneCell.RowIndex);
            }
            SaveTempCADdata();  // makes data dirty
        }

        private void CopyCadDataRow_button_Click(object sender, EventArgs e)
        {
            ClipBoardRow = CadData_GridView.CurrentRow;
            SaveTempCADdata();  // makes data dirty
        }

        private void PasteCadDataRow_button_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < CadData_GridView.ColumnCount; i++)
            {
                CadData_GridView.CurrentRow.Cells[i].Value = ClipBoardRow.Cells[i].Value;
            }
            SaveTempCADdata();  // makes data dirty
        }

        // =================================================================================
        // JobData editing
        // =================================================================================
        private void JobData_GridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            MakeJobDataDirty();
        }

        public string SelectedMethod { get; set; }
        public string PlacementMethod { get; set; }

        private void JobData_GridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                // user clicked header, most likely to sort for nozzles
                return;
            }

            if (JobData_GridView.CurrentCell.OwningColumn.Name == "JobdataMethodColumn")
            {
                // For method, show a form with explanation texts
                MakeJobDataDirty();
                MethodSelectionForm MethodDialog = new MethodSelectionForm(this);
                MethodDialog.ShowCheckBox = false;
                MethodDialog.ShowDialog(this);
                if (!string.IsNullOrEmpty(SelectedMethod))
                {
                    foreach (DataGridViewCell cell in JobData_GridView.SelectedCells)
                    {
                        JobData_GridView.Rows[cell.RowIndex].Cells["JobdataMethodColumn"].Value = SelectedMethod;
                        // JobData_GridView.Rows[cell.RowIndex].Cells["JobdataMethodParametersColumn"].Value = "--";
                        if ((SelectedMethod == "Fiducials") || (SelectedMethod == "LoosePart") || (SelectedMethod == "LoosePart Assisted"))
                        {
                            string FidAlg = SelectFiducialAlgorithm("--");
                            JobData_GridView.Rows[cell.RowIndex].Cells["JobdataMethodParametersColumn"].Value = FidAlg;
                        }
                    }
                }
                JobData_GridView.ClearSelection();
                Update_GridView(JobData_GridView);
                return;
            };

            if (JobData_GridView.CurrentCell.OwningColumn.Name == "JobdataMethodParametersColumn")
            {
                // For method parameter, show the tape selection form if method is place of some sort;
                MakeJobDataDirty();
                string TapeID;
                int TapeNo;
                int row = JobData_GridView.CurrentCell.RowIndex;
                if ((JobData_GridView.Rows[row].Cells["JobdataMethodColumn"].Value.ToString() == "Place") ||
                     (JobData_GridView.Rows[row].Cells["JobdataMethodColumn"].Value.ToString() == "Place Assisted") ||
                     (JobData_GridView.Rows[row].Cells["JobdataMethodColumn"].Value.ToString() == "Place Fast"))
                {
                    TapeID = SelectTape("Select tape for " + JobData_GridView.Rows[row].Cells["JobDataValueColumn"].Value.ToString()
                        + ", " + JobData_GridView.Rows[row].Cells["JobDataFootprintColumn"].Value.ToString());
                    if (TapeID == "none")
                    {
                        // user closed it
                        return;
                    }
                    JobData_GridView.Rows[row].Cells["JobdataMethodParametersColumn"].Value = TapeID;
                    if (Tapes.IdValidates_m(TapeID, out TapeNo))
                    {
                        JobData_GridView.Rows[row].Cells["JobDataNozzleColumn"].Value =
                            Tapes_dataGridView.Rows[TapeNo].Cells["Nozzle_Column"].Value.ToString();
                    }
                }
                // Auto fill the visual algorithm
                if ((JobData_GridView.Rows[row].Cells["JobdataMethodColumn"].Value.ToString() == "Fiducials") ||
                     (JobData_GridView.Rows[row].Cells["JobdataMethodColumn"].Value.ToString() == "LoosePart") ||
                     (JobData_GridView.Rows[row].Cells["JobdataMethodColumn"].Value.ToString() == "LoosePart Assisted"))
                {
                    JobData_GridView.Rows[row].Cells["JobdataMethodParametersColumn"].Value =
                        SelectFiducialAlgorithm(JobData_GridView.Rows[row].Cells["JobdataMethodParametersColumn"].Value.ToString());
                }
                JobData_GridView.ClearSelection();
                Update_GridView(JobData_GridView);
                return;
            }

            if (JobData_GridView.CurrentCell.OwningColumn.Name == "PlacementMethodColumn")
            {
                // as above, but for placement method
                MakeJobDataDirty();
                PlacementMethod = "--";
                PlacementMethodSelectionForm MethodDialog = new PlacementMethodSelectionForm(this);
                MethodDialog.StartPosition = FormStartPosition.CenterParent;
                MethodDialog.ShowDialog(this);
                foreach (DataGridViewCell cell in JobData_GridView.SelectedCells)
                {
                    JobData_GridView.Rows[cell.RowIndex].Cells["PlacementMethodColumn"].Value = PlacementMethod;
                    // Auto fill the visual algorithm
                    if (JobData_GridView.Rows[cell.RowIndex].Cells["PlacementMethodColumn"].Value.ToString() == "Up Cam Assisted")
                    {
                        JobData_GridView.Rows[cell.RowIndex].Cells["PlacementParameterColumn"].Value =
                            SelectFiducialAlgorithm(JobData_GridView.Rows[cell.RowIndex].Cells["PlacementParameterColumn"].Value.ToString());
                    }
                }
                JobData_GridView.ClearSelection();
                Update_GridView(JobData_GridView);
                return;
            }
            if (JobData_GridView.CurrentCell.OwningColumn.Name == "PlacementParameterColumn")
            {
                JobData_GridView.CurrentCell.Value = SelectFiducialAlgorithm(JobData_GridView.CurrentCell.Value.ToString());
            }
        }


        // If components are edited, update count automatically
        private void JobData_GridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            int col = JobData_GridView.CurrentCell.ColumnIndex;
            bool cancel;
            if (JobData_GridView.Columns[col].Name == "JobdataComponentsColumn")
            {
                // components
                MakeJobDataDirty();
                List<String> Line = SplitCSV(JobData_GridView.CurrentCell.Value.ToString(), ',', out cancel);
                int row = JobData_GridView.CurrentCell.RowIndex;
                JobData_GridView.Rows[row].Cells["JobdataCountColumn"].Value = Line.Count.ToString(CultureInfo.InvariantCulture);
                Update_GridView(JobData_GridView);
            }
        }

        public string SelectFiducialAlgorithm_FormResult = "--";

        private string SelectFiducialAlgorithm(string IntialValue)
        {
            SelectFiducialAlgorithm_FormResult = IntialValue;
            SelectFiducialAlgorithm_Form SelectForm = new SelectFiducialAlgorithm_Form(this);
            SelectForm.StartPosition = FormStartPosition.CenterParent;
            SelectForm.ShowDialog(this);
            return SelectFiducialAlgorithm_FormResult;
        }

        // =================================================================================
        // Do someting to a group of components:
        // =================================================================================
        // Several rows are selected at Job data:

        private void PlaceThese_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            this.ActiveControl = null; // User might need press enter during the process, which would run this again...
            if (!PrepareToPlace_m())
            {
                ShowMessageBox(
                    "Placement operation failed, nothing done.",
                    "Placement failed",
                    MessageBoxButtons.OK
                 );
                return;
            }

            DataGridViewRow Row;
            bool DoRow = false;
            // Get first row #...
            int FirstRow;
            for (FirstRow = 0; FirstRow < JobData_GridView.RowCount; FirstRow++)
            {
                Row = JobData_GridView.Rows[FirstRow];
                DoRow = false;
                foreach (DataGridViewCell oneCell in Row.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }
                if (DoRow)
                    break;
            }
            if (!DoRow)
            {
                CleanupPlacement(true, false);
                ShowMessageBox(
                    "Nothing selected, nothing done.",
                    "Done",
                    MessageBoxButtons.OK);
                return;
            };
            // ... so that we can put next row label in place:
            NextGroup_label.Text = JobData_GridView.Rows[FirstRow].Cells["JobDataValueColumn"].Value.ToString() +
                ", " + JobData_GridView.Rows[FirstRow].Cells["JobDataFootprintColumn"].Value.ToString() +
                " (" + JobData_GridView.Rows[FirstRow].Cells["JobdataCountColumn"].Value.ToString() + " pcs.)";

            // We know there is something to do, NextGroup_label is updated. Place all rows:
            bool PartAboveCam = false;
            for (int CurrentRow = 0; CurrentRow < JobData_GridView.RowCount; CurrentRow++)
            {
                // Find the Row we need to do now:
                Row = JobData_GridView.Rows[CurrentRow];
                DoRow = false;
                foreach (DataGridViewCell oneCell in Row.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }

                if (!DoRow)
                {
                    continue;
                };
                // Handle labels:
                PreviousGroup_label.Text = CurrentGroup_label.Text;
                CurrentGroup_label.Text = NextGroup_label.Text;
                // See if there is something more to do, so the "Next" label is corrently updated:
                bool NotLast = false;
                int NextRow;
                for (NextRow = CurrentRow + 1; NextRow < JobData_GridView.RowCount; NextRow++)
                {
                    foreach (DataGridViewCell someCell in JobData_GridView.Rows[NextRow].Cells)
                    {
                        if (someCell.Selected)
                        {
                            NotLast = true;
                            break;
                        }
                    }
                    if (NotLast)
                        break;
                }
                if (NotLast)
                {
                    NextGroup_label.Text = JobData_GridView.Rows[NextRow].Cells["JobDataValueColumn"].Value.ToString() +
                        ", " + JobData_GridView.Rows[NextRow].Cells["JobDataFootprintColumn"].Value.ToString() +
                        " (" + JobData_GridView.Rows[NextRow].Cells["JobdataCountColumn"].Value.ToString() + " pcs.)";
                }
                else
                {
                    NextGroup_label.Text = "--";
                };

                // Labels are updated, place the row:
                if (!PlaceRow_m(CurrentRow, out PartAboveCam))
                {
                    CleanupPlacement(false, PartAboveCam);
                    ShowMessageBox(
                        "Placement operation failed. Review job status.",
                        "Placement failed",
                        MessageBoxButtons.OK);
                    return;
                }
            };

            CleanupPlacement(true, PartAboveCam);
            ShowMessageBox(
                "Selected components succesfully placed.",
                "Done",
                MessageBoxButtons.OK);
        }


        // =================================================================================
        // This routine places selected component(s) from CAD data grid view.
        // The mechanism is the same as from job data: a temp row is addded to job data
        // and then that row is processed.
        private void PlaceOne_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            this.ActiveControl = null; // User might need press enter during the process, which would run this again...

            // Do we need to do something in the first place?
            // is something actually selected?
            if (CadData_GridView.SelectedCells.Count == 0)
            {
                DisplayText("Nothing selected.");
                return;
            };
            // Are the components already placed?
            DataGridViewRow CadRow;
            bool DoSomething = false;
            foreach (DataGridViewCell oneCell in CadData_GridView.SelectedCells)
            {
                DataGridViewCheckBoxCell cell = CadData_GridView.Rows[oneCell.RowIndex].Cells["CADdataPlacedColumn"] as DataGridViewCheckBoxCell;
                if (cell.Value != null)
                {
                    if (cell.Value.ToString().ToUpperInvariant() == "FALSE")
                    {
                        DoSomething = true;
                        break;
                    }
                }
            }
            if (!DoSomething)
            {
                DisplayText("Selected component(s) already placed.", KnownColor.DarkRed, true);
                return;
            }

            // OK, we actually need to do something.
            // Preparations:
            if (!PrepareToPlace_m())
            {
                ShowMessageBox(
                    "Placement operation failed, nothing done.",
                    "Placement failed",
                    MessageBoxButtons.OK
                 );
                return;
            };
            bool PartAboveCam = false;

            // if a cell is selected on a row, place that component:
            bool DoRow = false;
            bool ok = true;
            for (int CadRowNo = 0; CadRowNo < CadData_GridView.RowCount; CadRowNo++)
            {
                CadRow = CadData_GridView.Rows[CadRowNo];
                DoRow = false;
                foreach (DataGridViewCell oneCell in CadRow.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }
                if (!DoRow)
                {
                    continue;
                };
                // Found something to do. Find the row from Job data
                string component = CadRow.Cells["CADdataComponentColumn"].Value.ToString();
                int JobRowNo;
                bool cancel = false;
                for (JobRowNo = 0; JobRowNo < JobData_GridView.RowCount; JobRowNo++)
                {
                    List<String> componentlist = SplitCSV(JobData_GridView.Rows[JobRowNo].Cells["JobdataComponentsColumn"].Value.ToString(), ',', out cancel);
                    if (cancel)
                    {
                        return;
                    }
                    if (componentlist.Contains(component))
                    {
                        break;
                    }
                }
                if (JobRowNo >= JobData_GridView.RowCount)
                {
                    PumpOff();
                    ShowMessageBox(
                        "Did not find " + component + " from Job data.",
                        "Job data error",
                        MessageBoxButtons.OK);
                    return;
                }

                // is the component already placed?
                DataGridViewCheckBoxCell cell = CadData_GridView.Rows[CadRowNo].Cells["CADdataPlacedColumn"] as DataGridViewCheckBoxCell;
                if (cell.Value != null)
                {
                    if (cell.Value.ToString() == "True")
                    {
                        DisplayText(component + " already placed");
                        break;
                    }
                }

                // Make a copy of it to the end of the Job data grid view
                JobData_GridView.Rows.Add();
                int LastRowNo = JobData_GridView.Rows.Count - 1;
                for (int i = 0; i < JobData_GridView.ColumnCount; i++)
                {
                    JobData_GridView.Rows[LastRowNo].Cells[i].Value = JobData_GridView.Rows[JobRowNo].Cells[i].Value;
                }

                // Make the copy row to have only one component on it
                JobData_GridView.Rows[LastRowNo].Cells["JobdataCountColumn"].Value = "1";
                JobData_GridView.Rows[LastRowNo].Cells["JobdataComponentsColumn"].Value = component;
                // Update labels
                PreviousGroup_label.Text = CurrentGroup_label.Text;
                CurrentGroup_label.Text = JobData_GridView.Rows[LastRowNo].Cells["JobDataValueColumn"].Value.ToString() +
                    ", " + JobData_GridView.Rows[LastRowNo].Cells["JobDataFootprintColumn"].Value.ToString() + " (1 pcs.)";
                // Place that row
                ok = PlaceRow_m(LastRowNo, out PartAboveCam);
                // delete the row
                JobData_GridView.Rows.RemoveAt(LastRowNo);
                if (!ok)
                {
                    break;
                }
            }
            CleanupPlacement(ok, PartAboveCam);
            Update_GridView(JobData_GridView);
            SaveTempCADdata();
        }

        // =================================================================================
        // Checks if the component is placed already
        // returns success of operation, sets placed status to placed
        private bool AlreadyPlaced_m(string component, ref bool placed)
        {
            // find the row
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                if (Row.Cells["CADdataComponentColumn"].Value.ToString() == component)
                {
                    DataGridViewCheckBoxCell cell = Row.Cells["CADdataPlacedColumn"] as DataGridViewCheckBoxCell;
                    if (cell.Value != null)
                    {
                        if (cell.Value.ToString().ToUpperInvariant() == "TRUE")
                        {
                            placed = true;
                        }
                        else
                        {
                            placed = false;
                        }
                    }
                    return true;
                }
            }
            ShowMessageBox(
                "Component " + component + "not found in CAD data. CAD data and job data don't match",
                "CAD data vs job data mismatch",
                MessageBoxButtons.OK);
            return false;
        }

        // =================================================================================
        // This routine places the [index] row from Job data grid view:
        private bool PlaceRow_m(int RowNo, out bool PartAboveCam)
        {
            PartAboveCam = false;
            DisplayText("PlaceRow_m(" + RowNo.ToString(CultureInfo.InvariantCulture) + ")", KnownColor.Blue);
            // Select the row and keep it visible
            JobData_GridView.Rows[RowNo].Selected = true;
            HandleGridScrolling(false, JobData_GridView);

            string[] Components;  // This array has the list of components to place:
            string NewID = "";
            bool RestoreRow = false;
            // RestoreRow: If we'll replace the jobdata row with new data at the end. 
            // There isn't any new data, unless method is "?".

            // If the Method is "?", ask the user what to do:
            DataGridViewRow tempRow = (DataGridViewRow)JobData_GridView.Rows[RowNo].Clone();
            if (JobData_GridView.Rows[RowNo].Cells["JobdataMethodColumn"].Value.ToString() == "?")
            {
                // We'll now get new data from the user. By default, we don't mess with the data we have
                // So, take a copy of current row
                RestoreRow = true;
                for (int i = 0; i < tempRow.Cells.Count; i++)
                {
                    tempRow.Cells[i].Value = JobData_GridView.Rows[RowNo].Cells[i].Value;
                }

                // Display Method selection form:
                bool UserHasNotDecided = false;
                MethodSelectionForm MethodDialog = new MethodSelectionForm(this);
                do
                {
                    MethodDialog.ShowCheckBox = true;
                    MethodDialog.HeaderString = CurrentGroup_label.Text;
                    MethodDialog.ShowDialog(this);
                    if (Setting.Placement_UpdateJobGridAtRuntime)
                    {
                        RestoreRow = false;
                    };
                    if ((SelectedMethod == "Place") || (SelectedMethod == "Place Assisted") || (SelectedMethod == "Place Fast"))
                    {
                        // show the tape selection dialog
                        NewID = SelectTape("Select tape for " + JobData_GridView.Rows[RowNo].Cells["JobDataValueColumn"].Value.ToString()
                            + ", " + JobData_GridView.Rows[RowNo].Cells["JobDataFootprintColumn"].Value.ToString());
                        if (!Setting.Placement_UpdateJobGridAtRuntime)
                        {
                            RestoreRow = true;   // In case user unselected it at the tape selection dialog
                        };

                        if (NewID == "none")
                        {
                            UserHasNotDecided = true; // User did select "Place", but didn't select TapeNumber, we'll ask again.
                        }
                        else if (NewID == "Ignore")
                        {
                            SelectedMethod = "Ignore";
                            NewID = "";
                        }
                        else if (NewID == "Abort")
                        {
                            SelectedMethod = "Ignore";
                            NewID = "";
                            AbortPlacement = true;
                            AbortPlacementShown = true;
                            RestoreRow = true;		// something went astray, keep method at "?"
                        }
                    }

                } while (UserHasNotDecided);
                if (string.IsNullOrEmpty(SelectedMethod))
                {
                    return false;   // user pressed x
                }
                // Put the values to JobData_GridView
                JobData_GridView.Rows[RowNo].Cells["JobdataMethodColumn"].Value = SelectedMethod;
                JobData_GridView.Rows[RowNo].Cells["JobdataMethodParametersColumn"].Value = NewID;
                Update_GridView(JobData_GridView);
                MethodDialog.Dispose();
            }
            // Method is now selected, even if it was ?. If user quits the operation, PlaceComponent_m() notices.

            // The place operation does not necessarily have any components for it (such as a manual nozzle change).
            // Make sure there is valid data at ComponentList anyway.
            if (JobData_GridView.Rows[RowNo].Cells["JobdataComponentsColumn"].Value == null)
            {
                JobData_GridView.Rows[RowNo].Cells["JobdataComponentsColumn"].Value = "--";
            };
            if (JobData_GridView.Rows[RowNo].Cells["JobdataComponentsColumn"].Value.ToString() == "--")
            {
                Components = new string[] { "--" };
            }
            else
            {
                Components = JobData_GridView.Rows[RowNo].Cells["JobdataComponentsColumn"].Value.ToString().Split(',');
            };
            bool ReturnValue = true;

            // Prepare for placement
            string method = JobData_GridView.Rows[RowNo].Cells["JobdataMethodColumn"].Value.ToString();
            int nozzle;
            // Check, that the row isn't placed already
            bool EverythingPlaced = true;
            bool thisPlaced = true;
            int placedCount = 0;
            if ((method == "Place Fast") || (method == "Place") || (method == "LoosePart") || (method == "LoosePart Assisted") || (method == "Place Assisted"))
            {
                foreach (string component in Components)
                {
                    if (!AlreadyPlaced_m(component, ref thisPlaced))
                    {
                        return false;
                    }
                    if (!thisPlaced)
                    {
                        EverythingPlaced = false;
                    }
                    else
                    {
                        placedCount++;
                    }
                }
                if (EverythingPlaced)
                {
                    DisplayText("All " + JobData_GridView.Rows[RowNo].Cells["JobDataValueColumn"].Value.ToString()
                        + ", " + JobData_GridView.Rows[RowNo].Cells["JobDataFootprintColumn"].Value.ToString() + " already placed.", KnownColor.DarkRed);
                    return true;
                }
            }
            // Check nozzle, change if needed
            // if we are using a method that potentially needs a nozzle and automatic change is enabled:
            if (
                ((method == "Place Fast") || (method == "Place") || (method == "LoosePart") || (method == "LoosePart Assisted") || (method == "Place Assisted"))
                && Setting.Nozzles_Enabled)
            {
                if (JobData_GridView.Rows[RowNo].Cells["JobDataNozzleColumn"].Value == null)
                {
                    nozzle = Setting.Nozzles_default;
                }
                else
                {
                    if (!int.TryParse(JobData_GridView.Rows[RowNo].Cells["JobDataNozzleColumn"].Value.ToString(), out nozzle))
                    {
                        ShowMessageBox(
                            "Bad data at Nozzle column for " + JobData_GridView.Rows[RowNo].Cells["JobDataValueColumn"].Value.ToString()
                            + ", " + JobData_GridView.Rows[RowNo].Cells["JobDataFootprintColumn"].Value.ToString(),
                            "Nozzle?",
                            MessageBoxButtons.OK);
                        return false;
                    }
                    if ((nozzle < 1) || (nozzle > Setting.Nozzles_count))
                    {
                        ShowMessageBox(
                            "Invalid value at Nozzle column for " + JobData_GridView.Rows[RowNo].Cells["JobDataValueColumn"].Value.ToString()
                            + ", " + JobData_GridView.Rows[RowNo].Cells["JobDataFootprintColumn"].Value.ToString(),
                            "Nozzle?",
                            MessageBoxButtons.OK);
                        return false;
                    }
                }
                if (!ChangeNozzle_m(nozzle))
                {
                    return false;
                }
            }

            bool FirstInRow = true;
            if (method == "Place Fast")
            {
                string TapeID = JobData_GridView.Rows[RowNo].Cells["JobdataMethodParametersColumn"].Value.ToString();
                int count;
                if (!int.TryParse(JobData_GridView.Rows[RowNo].Cells["JobdataCountColumn"].Value.ToString(), out count))
                {
                    ShowMessageBox(
                        "Bad data at component count",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                }
                if (count <= placedCount)
                {
                    count = 1;
                }
                else
                {
                    count -= placedCount;
                }
                if (!Tapes.PrepareForFastPlacement_m(TapeID, count))
                {
                    return false;
                }
                Tapes.FastParametersOk = true;
            }
            // Place parts:
            PartAboveCam = false;
            foreach (string Component in Components)
            {
                if (!PlaceComponent_m(Component, RowNo, FirstInRow, out PartAboveCam))
                {
                    JobData_GridView.Rows[RowNo].Selected = false;
                    ReturnValue = false;
                    Tapes.FastParametersOk = false;
                    break;
                };
                FirstInRow = false;
            };
            Tapes.FastParametersOk = false;

            // restore the row if needed
            if (RestoreRow)
            {
                for (int i = 0; i < tempRow.Cells.Count; i++)
                {
                    JobData_GridView.Rows[RowNo].Cells[i].Value = tempRow.Cells[i].Value;
                }
                Update_GridView(JobData_GridView);
            };

            JobData_GridView.Rows[RowNo].Selected = false;
            return ReturnValue;
        }


        // =================================================================================
        // All rows:
        private void PlaceAll_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;
            
            this.ActiveControl = null; // User might need press enter during the process, which would run this again...

            if (!PrepareToPlace_m())
            {
                ShowMessageBox(
                    "Placement operation failed, nothing done.",
                    "Placement failed",
                    MessageBoxButtons.OK
                 );
                return;
            }
            PreviousGroup_label.Text = "--";
            CurrentGroup_label.Text = "--";

            NextGroup_label.Text = JobData_GridView.Rows[0].Cells["JobDataValueColumn"].Value.ToString()
                +", " + JobData_GridView.Rows[0].Cells["JobDataFootprintColumn"].Value.ToString()
                +" (" + JobData_GridView.Rows[0].Cells["JobdataCountColumn"].Value.ToString() + " pcs.)";
            
            bool PartAboveCam = false;
            bool ok = true;
            for (int i = 0; i < JobData_GridView.RowCount; i++)
            {
                PreviousGroup_label.Text = CurrentGroup_label.Text;
                CurrentGroup_label.Text = NextGroup_label.Text;
                if (i < (JobData_GridView.RowCount - 1))
                {
                    NextGroup_label.Text = JobData_GridView.Rows[i + 1].Cells["JobDataValueColumn"].Value.ToString()
                        + ", " + JobData_GridView.Rows[i + 1].Cells["JobDataFootprintColumn"].Value.ToString()
                        + " (" + JobData_GridView.Rows[i + 1].Cells["JobdataCountColumn"].Value.ToString() + " pcs.)";
                }
                else
                {
                    NextGroup_label.Text = "--";
                };

                if (!PlaceRow_m(i, out PartAboveCam))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                CleanupPlacement(ok, PartAboveCam);
                ShowMessageBox(
                    "All components placed.",
                    "Done",
                    MessageBoxButtons.OK);
            }
            else
            {
                CleanupPlacement(ok, PartAboveCam);
                ShowMessageBox(
                    "Operation stopped by user.",
                    "Done",
                    MessageBoxButtons.OK);
            }
        }


        // =================================================================================

        // =================================================================================
        // ComponentDataValidates_m():
        // Checks that CAD data for a component is good. (Used by PlaceComponent_m() )

        private bool ComponentDataValidates_m(string Component, int CADdataRow, int GroupRow)
        {
            // Component exist. Footprint, X, Y and rotation data should be there:
            if (JobData_GridView.Rows[GroupRow].Cells["JobDataValueColumn"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No value data",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }

            if (JobData_GridView.Rows[GroupRow].Cells["JobDataFootprintColumn"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No Footprint",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }

            if (CadData_GridView.Rows[CADdataRow].Cells["CADdataXnominalColumn"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No X data",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }

            if (CadData_GridView.Rows[CADdataRow].Cells["CADdataYnominalColumn"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No Y data",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }

            if (CadData_GridView.Rows[CADdataRow].Cells["CADdataRotationNominalColumn"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No Rotation data",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }
            else
            {
                double Rotation;
                if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataRotationNominalColumn"].Value.ToString().Replace(',', '.'), out Rotation))
                {
                    ShowMessageBox(
                        "Component " + Component + ": Bad Rotation data",
                        "Conversion Error",
                        MessageBoxButtons.OK);
                    return false;
                }
            }

            // Machine coordinate data should be also there, properly formatted:
            double X;
            double Y;
            double A;
            if ((!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataXmachineColumn"].Value.ToString().Replace(',', '.'), out X))
                ||
                (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataYmachineColumn"].Value.ToString().Replace(',', '.'), out Y)))
            {
                ShowMessageBox(
                    "Component " + Component + ", bad machine coordinate",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            }
            if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataRotationMachineColumn"].Value.ToString().Replace(',', '.'), out A))
            {
                ShowMessageBox(
                    "Bad data at Rotation, machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            };

            // Even if component is not specified, Method data should be there:
            if (JobData_GridView.Rows[GroupRow].Cells["JobdataMethodColumn"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ", No method data",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                return false;
            }
            return true;
        } // end of ComponentDataValidates_m()


        // =================================================================================
        // PlaceComponent_m()
        // This routine does the actual placement of a single component.
        // Component is the component name (Such as R15); based to this, we'll find the coordinates from CAD data
        // GroupRow is the row index to Job data grid view.
        // =================================================================================

        private bool PlaceComponent_m(string Component, int GroupRow, bool FirstInRow, out bool PartAboveCam)
        {
            PartAboveCam = false;
            DisplayText("PlaceComponent_m: Component: " + Component + ", Row:" + GroupRow.ToString(CultureInfo.InvariantCulture), KnownColor.Blue);
            // Skip fiducials
            if (JobData_GridView.Rows[GroupRow].Cells["JobdataMethodColumn"].Value.ToString() == "Fiducials")
            {
                DisplayText("PlaceComponent_m(): Skip fiducials");
                return true;
            }
            // If component is specified, find its row in CAD data:
            int CADdataRow = -1;
            if (Component != "--")
            {
                foreach (DataGridViewRow Row in CadData_GridView.Rows)
                {
                    if (Row.Cells["CADdataComponentColumn"].Value.ToString() == Component)
                    {
                        CADdataRow = Row.Index;
                        break;
                    }
                }
                if (CADdataRow < 0)
                {
                    ShowMessageBox(
                        "Component " + Component + " data not found.",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                }
            }

            // Validate data:
            string Footprint = "n/a";
            string Xstr = "n/a";
            string Ystr = "n/a";
            string RotationStr = "n/a";
            double X_machine = 0;
            double Y_machine = 0;
            double A_machine = 0;
            string Method = "";
            string MethodParameter = "";

            if (Component != "--") // if component exists:
            {
                if (SkipMeasurements_checkBox.Checked)
                {
                    // User wants to use the nominal coordinates. Copy the nominal to machine for this to happen:
                    if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataXnominalColumn"].Value.ToString().Replace(',', '.'), out double X))
                    {
                        DisplayText("Bad data X nominal at component " + Component);
                    }
                    X = X + Setting.General_JigOffsetX + Setting.Job_Xoffset;
                    CadData_GridView.Rows[CADdataRow].Cells["CADdataXmachineColumn"].Value = X.ToString(CultureInfo.InvariantCulture);

                    if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataYnominalColumn"].Value.ToString().Replace(',', '.'), out double Y))
                    {
                        DisplayText("Bad data Y nominal at component " + Component);
                    }
                    Y = Y + Setting.General_JigOffsetY + Setting.Job_Yoffset;
                    CadData_GridView.Rows[CADdataRow].Cells["CADdataYmachineColumn"].Value = Y.ToString(CultureInfo.InvariantCulture);

                    CadData_GridView.Rows[CADdataRow].Cells["CADdataRotationMachineColumn"].Value = CadData_GridView.Rows[CADdataRow].Cells["CADdataRotationNominalColumn"].Value;
                }
                // check data consistency
                if (!ComponentDataValidates_m(Component, CADdataRow, GroupRow))
                {
                    return false;
                }
                // and fill values:
                Footprint = JobData_GridView.Rows[GroupRow].Cells["JobDataValueColumn"].Value.ToString() 
                    + ", " + JobData_GridView.Rows[GroupRow].Cells["JobDataFootprintColumn"].Value.ToString();
                Xstr = CadData_GridView.Rows[CADdataRow].Cells["CADdataXnominalColumn"].Value.ToString();
                Ystr = CadData_GridView.Rows[CADdataRow].Cells["CADdataYnominalColumn"].Value.ToString();
                RotationStr = CadData_GridView.Rows[CADdataRow].Cells["CADdataRotationNominalColumn"].Value.ToString();
                double tempD;
                if (double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataXmachineColumn"].Value.ToString().Replace(',', '.'), out tempD))
                {
                    X_machine = tempD;
                }
                else
                {
                    DisplayText("Bad data X machine at component " + Component);
                    return false;
                }
                if (double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataYmachineColumn"].Value.ToString().Replace(',', '.'), out tempD))
                {
                    Y_machine = tempD;
                }
                else
                {
                    DisplayText("Bad data Y machine at component " + Component);
                    return false;
                }
                if (double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["CADdataRotationMachineColumn"].Value.ToString().Replace(',', '.'), out tempD))
                {
                    A_machine = tempD;
                }
                else
                {
                    DisplayText("Bad data A machine at component " + Component);
                    return false;
                }
            }

            Method = JobData_GridView.Rows[GroupRow].Cells["JobdataMethodColumn"].Value.ToString();
            // Even if component is not specified, Method data should be there:
            // it is not an error if method does not have parameters.
            if (JobData_GridView.Rows[GroupRow].Cells["JobdataMethodParametersColumn"].Value != null)
            {
                MethodParameter = JobData_GridView.Rows[GroupRow].Cells["JobdataMethodParametersColumn"].Value.ToString();
            };

            // Data is now validated, all variables have values that check out. Place the component.
            // Update "Now placing" labels:
            if ((Method == "LoosePart") || (Method == "LoosePart Assisted") || (Method == "Place") || (Method == "Place Assisted") || (Method == "Place Fast"))
            {
                PlacedComponent_label.Text = Component;
                PlacedComponent_label.Update();
                PlacedValue_label.Text = Footprint;
                PlacedValue_label.Update();
                PlacedX_label.Text = Xstr;
                PlacedX_label.Update();
                PlacedY_label.Text = Ystr;
                PlacedY_label.Update();
                PlacedRotation_label.Text = RotationStr;
                PlacedRotation_label.Update();
                MachineCoords_label.Text = "( " +
                    CadData_GridView.Rows[CADdataRow].Cells["CADdataXmachineColumn"].Value.ToString() + ", " +
                    CadData_GridView.Rows[CADdataRow].Cells["CADdataYmachineColumn"].Value.ToString() + " )";
                MachineCoords_label.Update();
            };

            if (AbortPlacement)
            {
                StopOperation();
                return false;
            }

            // Component is at CadData_GridView.Rows[CADdataRow]. 
            // What to do to it is at  JobData_GridView.Rows[GroupRow].

            int time;
            switch (Method)
            {
                case "?":
                    ShowMessageBox(
                        "Method is ? at run time",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;

                case "Pause":
                    ShowMessageBox(
                        "Job pause, click OK to continue.",
                        "Pause",
                        MessageBoxButtons.OK);
                    return true;

                case "DownCam Snapshot":
                case "UpCam Snapshot":
                case "LoosePart":
                case "LoosePart Assisted":
                case "Place Fast":
                case "Place":
                case "Place Assisted":
                    if (Component == "--")
                    {
                        ShowMessageBox(
                            "Attempt to \"place\" non-existing component(\"--\")",
                            "Data error",
                            MessageBoxButtons.OK);
                        return false;
                    }
                    DataGridViewCheckBoxCell cell = CadData_GridView.Rows[CADdataRow].Cells["CADdataPlacedColumn"] as DataGridViewCheckBoxCell;
                    if (cell.Value != null)
                    {
                        if (cell.Value.ToString() == "True")
                        {
                            DisplayText(Component + " already placed");
                            break;
                        }
                    }
                    PartAboveCam = false;
                    if (!PlacePart_m(CADdataRow, GroupRow, X_machine, Y_machine, A_machine, FirstInRow, out PartAboveCam))
                    {
                        return false;
                    }
                    else
                    {
                        CadData_GridView.Rows[CADdataRow].Cells["CADdataPlacedColumn"].Value = true;
                        SaveTempCADdata();
                    }
                    break;

                case "Change Nozzle":
                    if (!ChangeNozzleManually_m())
                        return false;
                    break;

                case "Recalibrate":
                    if (!CNC_XYA_m(0.0, 0.0, Cnc.CurrentA))
                    {
                        return false;
                    }
                    OpticalHoming_m();
                    ValidMeasurement_checkBox.Checked = false;
                    if (!PrepareToPlace_m())
                    {
                        return false;
                    }
                    break;

                case "Ignore":
                    // Optionally, method parameter specifies pause time (used in debugging, I can't think of a real-life use case for that)
                    if (int.TryParse(JobData_GridView.Rows[GroupRow].Cells["JobdataMethodParametersColumn"].Value.ToString(), out time))
                    {
                        for (int z = 0; z < time / 5; z++)
                        {
                            Application.DoEvents();  // keeping video running
                            Thread.Sleep(5);
                        }
                    };
                    return true;  // To next row...
                // break;

                case "Fiducials":
                    return true;  // To next row...
                // break; 


                // Used this for debugging, but is unreachable now
                case "Locate":
                    // Shows the component and its footprint(if known)...
                    CNC_XYA_m(X_machine, Y_machine, Cnc.CurrentA);

                    // ... either for the time specified in method parameter
                    if (int.TryParse(JobData_GridView.Rows[GroupRow].Cells["JobdataMethodParametersColumn"].Value.ToString(), out time))
                    {
                        for (int z = 0; z < time / 5; z++)
                        {
                            Application.DoEvents();  // keeping video running
                            Thread.Sleep(5);
                        }
                    }
                    else
                    {
                        // If no time specified, show a dialog:
                        ShowMessageBox(
                            "This is " + Component,
                            "Locate Component",
                            MessageBoxButtons.OK);
                    }
                    DownCamera.DrawBox = false;
                    break;

                default:
                    ShowMessageBox(
                        "No code for method " + JobData_GridView.Rows[GroupRow].Cells["JobdataMethodColumn"].Value.ToString(),
                        "Lazy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                // break;
            }
            return true;
        }

        public bool AbortPlacement { get; set; } = false;
        public bool AbortPlacementShown { get; set; } = false;

        // =================================================================================
        private bool ChangeNozzleManually_m()
        {
            Cnc.DisableZswitches();
            PumpOff();
            Cnc.MotorPowerOff();
            ShowMessageBox(
                "Change Nozzle now, press OK when done",
                "Nozzle change pause",
                MessageBoxButtons.OK);
            Cnc.MotorPowerOn();
            Zlim_checkBox.Checked = true;
            Zhome_checkBox.Checked = true;
            Nozzle.NozzleDataAllNozzles[Setting.Nozzles_current-1].Calibrated = false;
            NozzlesParameters_dataGridView.Rows[Setting.Nozzles_current - 1].Cells["NozzleCalibrated_Column"].Value = false;
            Update_GridView(NozzlesParameters_dataGridView);
            ValidMeasurement_checkBox.Checked = false;
            Cnc.EnableZswitches();
            if (!MechanicalHoming_m())
            {
                return false;
            }
            if (!OpticalHoming_m())
            {
                return false;
            }
            if (!CalibrateNozzle_m())
            {
                return false;
            }
            if (!BuildMachineCoordinateData_m())
            {
                return false;
            }
            PumpOn();
            return true;
        }

        // =================================================================================
        // This is called before any placement is done:
        // =================================================================================
        private bool PrepareToPlace_m()
        {
            if (JobData_GridView.RowCount < 1)
            {
                ShowMessageBox(
                    "No Job loaded.",
                    "No Job",
                    MessageBoxButtons.OK
                );
                return false;
            }

            if(!ValidMeasurement_checkBox.Checked)
            {
                CurrentGroup_label.Text = "Measuring PCB";
                if (!BuildMachineCoordinateData_m())
                {
                    CurrentGroup_label.Text = "--";
                    return false;
                }
            }

            AbortPlacement = false;
            AbortPlacementShown = false;
            PlaceThese_button.Capture = false;
            PlaceAll_button.Capture = false;
            JobData_GridView.ReadOnly = true;
            PumpOn();
            return true;
        }  // end PrepareToPlace_m

        // =================================================================================
        // This cleans up the UI after placement operations
        // =================================================================================
        private void CleanupPlacement(bool success, bool PartAboveCam)
        {
            PlacedComponent_label.Text = "--";
            PlacedValue_label.Text = "--";
            PlacedX_label.Text = "--";
            PlacedY_label.Text = "--";
            PlacedRotation_label.Text = "--";
            MachineCoords_label.Text = "( -- )";
            PreviousGroup_label.Text = "--";
            CurrentGroup_label.Text = "--";
            NextGroup_label.Text = "--";
            JobData_GridView.ReadOnly = false;
            // if fail (!success) it helps if machine stays still. However,
            // if up cam assisted placement has part above camera, take the part away...
            if (success || PartAboveCam)
            {
                CNC_Park();
            }
            if (!PartAboveCam)      // .. but don't release it (user can take it above up cam and debug)
            {
                Cnc.PumpDefaultSetting();
                Cnc.VacuumDefaultSetting();
            }
            SelectCamera(DownCamera);
        }


        // =================================================================================
        // PickUpThis_m(): Actual pickup, assumes Nozzle is on top of the part
        private bool PickUpThis_m(int TapeNumber)
        {
            string Z_str = Tapes_dataGridView.Rows[TapeNumber].Cells["Z_Pickup_Column"].Value.ToString();
            if (Z_str == "--")
            {
                DisplayText("PickUpPart_m(): Probing pickup Z", KnownColor.Blue);
                if (!Nozzle_ProbeDown_m(Setting.Placement_Pickup_Depth))
                {
                    return false;
                }
                double Zpickup = Cnc.CurrentZ - Setting.Placement_Pickup_Depth;
                Tapes_dataGridView.Rows[TapeNumber].Cells["Z_Pickup_Column"].Value = Zpickup.ToString(CultureInfo.InvariantCulture);
                DisplayText("PickUpPart_m(): Probed Z= " + Cnc.CurrentZ.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                double Z;
                if (!double.TryParse(Z_str.Replace(',', '.'), out Z))
                {
                    ShowMessageBox(
                        "Bad pickup Z data at Tape #" + TapeNumber.ToString(CultureInfo.InvariantCulture),
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                };
                Z += Setting.Placement_Pickup_Depth;
                DisplayText("PickUpPart_m(): Part pickup, Z" + Z.ToString(CultureInfo.InvariantCulture), KnownColor.Blue);
                if (!CNC_Z_m(Z))
                {
                    return false;
                }
            }
            VacuumOn();
            DisplayText("PickUpPart_m(): Nozzle up");
            if (!CNC_Z_m(0))
            {
                return false;
            }
            return true;
        }

        // ========================================================================================
        public bool PickUpPartFast_m(int TapeNum)
        {
            if (UseCoordinatesDirectly(TapeNum))
            {
                return (PickUpPartWithDirectCoordinates_m(TapeNum));
            }

            if (!Tapes.FastParametersOk)
            {
                ShowMessageBox(
                    "FastParameters not ok",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            }

            // find the part location and go there:
            double PartX = 0.0;
            double PartY = 0.0;
            double A = 0.0;

            // GetPartLocationFromHolePosition_m calculates A correctly, but we have already figured out X and Y
            if (!Tapes.GetPartLocationFromHolePosition_m(TapeNum, Tapes.FastXpos, Tapes.FastYpos, out PartX, out PartY, out A))
            {
                ShowMessageBox(
                    "Can't find tape hole",
                    "Tape error",
                    MessageBoxButtons.OK
                );
            }

            // Now, PartX, PartY, A tell the position of the part.
            if (!Nozzle.Move_m(PartX, PartY, A))
            {
                return false;
            }
            if (!PickUpThis_m(TapeNum))
            {
                return false;
            }

            if (!Tapes.IncrementTape_Fast_m(TapeNum))
            {
                return false;
            }

            if (AbortPlacement)
            {
                StopOperation();
                return false;
            }
            return true;
        }

        // =================================================================================
        // PickUpPartWithHoleMeasurement_m(): Picks next part from the tape, measuring the hole
        private bool PickUpPartWithHoleMeasurement_m(int TapeNumber)
        {
            if (UseCoordinatesDirectly(TapeNumber))
            {
                return (PickUpPartWithDirectCoordinates_m(TapeNumber));
            }

            // If this succeeds, we update next hole location at the end, but these values are measured at start
            double HoleX = 0;
            double HoleY = 0;
            DisplayText("PickUpPart_m(), tape no: " + TapeNumber.ToString(CultureInfo.InvariantCulture));
            // Go to part location:
            VacuumOff();
            if (!Tapes.GotoNextPartByMeasurement_m(TapeNumber, out HoleX, out HoleY))
            {
                return false;
            }
            // Pick it up:
            if (!PickUpThis_m(TapeNumber))
            {
                return false;
            }

            if (!Tapes.IncrementTape(TapeNumber, HoleX, HoleY))
            {
                return false;
            }

            if (AbortPlacement)
            {
                if (!AbortPlacementShown)
                {
                    AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                AbortPlacement = false;
                return false;
            }
            return true;
        }

        // =================================================================================
        // PickUpPartWithDirectCoordinates_m(): Picks next part, using coordinates directly
        // First, some helper functions:

        public bool UseCoordinatesDirectly(int TapeNum)
        {
            DataGridViewCheckBoxCell cell = Tapes_dataGridView.Rows[TapeNum].Cells["CoordinatesForParts_Column"] as DataGridViewCheckBoxCell;
            if (cell.Value != null)
            {
                if (cell.Value.ToString() == "True")
                {
                    return true;
                }
            }
            return false;
        }

        public bool UseNozzleCoordinates(int TapeNum)
        {
            DataGridViewCheckBoxCell cell = Tapes_dataGridView.Rows[TapeNum].Cells["UseNozzleCoordinates_Column"] as DataGridViewCheckBoxCell;
            if (cell.Value != null)
            {
                if (cell.Value.ToString() == "True")
                {
                    return true;
                }
            }
            return false;
        }

        private bool FindPartWithDirectCoordinates_m(int TapeNum, out double X, out double Y, out double A, out bool increment)
        {
            X = 0.0;
            Y = 0.0;
            A = 0.0;
            increment = false;

            double FirstX;
            double FirstY;

            if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["FirstX_Column"].Value.ToString().Replace(',', '.'), out FirstX))
            {
                DisplayText("Bad data, X column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["FirstY_Column"].Value.ToString().Replace(',', '.'), out FirstY))
            {
                DisplayText("Bad data, Y column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }
            double LastX = 0.0;
            double LastY = 0.0;
            // Not all values need to be set, but if they are set, they need to be valid
            if (Tapes_dataGridView.Rows[TapeNum].Cells["LastX_Column"].Value != null)
            {
                if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["LastX_Column"].Value.ToString().Replace(',', '.'), out LastX))
                {
                    DisplayText("Bad data, Last X column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
            }
            if (Tapes_dataGridView.Rows[TapeNum].Cells["LastY_Column"].Value != null)
            {
                if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["LastY_Column"].Value.ToString().Replace(',', '.'), out LastY))
                {
                    DisplayText("Bad data, Last Y column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
            }
            double pitch;
            if (Tapes_dataGridView.Rows[TapeNum].Cells["Pitch_Column"].Value == null)
            {
                DisplayText("Bad data, Pitch column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["Pitch_Column"].Value.ToString().Replace(',', '.'), out pitch))
            {
                DisplayText("Bad data, Pitch column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }

            if (
                (Math.Abs(pitch) < 0.00001) ||
                ((Math.Abs(LastX) < 0.00001) && (Math.Abs(LastY) < 0.00001))
               )
            {
                // no increment, use first coordinates directly
                X = FirstX;
                Y = FirstY;
            }
            else
            {
                // more than one part defined, need to tell the caller the next column needs to be incremented if operation is succesful
                increment = true;
                // calculate location
                int increments;
                if (!int.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["NextPart_Column"].Value.ToString(), out increments))
                {
                    DisplayText("Bad data, Next column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
                increments = increments - 1;
                if (increments==0)
                {
                    X = FirstX;
                    Y = FirstY;
                }
                else
                {
                    double mag = Math.Sqrt(((LastX - FirstX) * (LastX - FirstX)) + ((LastY - FirstY) * (LastY - FirstY)));
                    double Xincr = (LastX - FirstX) * pitch / mag;
                    double Yincr = (LastY - FirstY) * pitch / mag;
                    X = FirstX + Xincr * increments;
                    Y = FirstY + Yincr * increments;
                }
            }

            if (Tapes_dataGridView.Rows[TapeNum].Cells["RotationDirect_Column"].Value != null)
            {
                if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["RotationDirect_Column"].Value.ToString().Replace(',', '.'), out A))
                {
                    DisplayText("Bad data, A correction column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
            }
            return true;
        }

        private bool PickUpPartWithDirectCoordinates_m(int TapeNum)
        {
            double X;
            double Y;
            double A;
            bool increment;

            if (!FindPartWithDirectCoordinates_m(TapeNum, out X, out Y, out A, out increment))
            {
                return false;
            }
            DisplayText("PickUpPartWithDirectCoordinates_m(), tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString()
                + ", X: " + X.ToString("0.000", CultureInfo.InvariantCulture)
                + ", Y: " + Y.ToString("0.000", CultureInfo.InvariantCulture)
                + ", A: " + A.ToString("0.000", CultureInfo.InvariantCulture));

            if (UseNozzleCoordinates(TapeNum))
            {
                if (!CNC_XYA_m(X, Y, A))
                {
                    return false;
                }
            }
            else
            {
                if (!Nozzle.Move_m(X, Y, A))
                {
                    return false;
                }
            }
            if (!PickUpThis_m(TapeNum))
            {
                return false;
            }

            if (increment)
            {
                int i;
                if (int.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["NextPart_Column"].Value.ToString(), out i))
                {
                    i++;
                    Tapes_dataGridView.Rows[TapeNum].Cells["NextPart_Column"].Value = i.ToString(CultureInfo.InvariantCulture);
                }
                // we know it parses, but compiler wants us to check enayway; no else clause is needed.
            }
            return true;
        }

        // =================================================================================
        // PutPartDown_m(): Puts part down at this position. 
        // If placement Z isn't known already, updates the tape info.
        private bool PutPartDown_m(int TapeNum)
        {
            string Z_str = Tapes_dataGridView.Rows[TapeNum].Cells["Z_Place_Column"].Value.ToString();
            if (Z_str == "--")
            {
                DisplayText("PutPartDown_m(): Probing placement Z", KnownColor.Blue);
                if (!Nozzle_ProbeDown_m(Setting.Placement_Placement_Depth))
                {
                    return false;
                };
                double Zplace = Cnc.CurrentZ - Setting.Placement_Placement_Depth;
                Tapes_dataGridView.Rows[TapeNum].Cells["Z_Place_Column"].Value = Zplace.ToString(CultureInfo.InvariantCulture);
                DisplayText("PutPartDown_m(): Probed placement Z= " + Cnc.CurrentZ.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                double Z;
                if (!double.TryParse(Z_str.Replace(',', '.'), out Z))
                {
                    ShowMessageBox(
                        "Bad place Z data at Tape #" + TapeNum,
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                };
                Z += Setting.Placement_Placement_Depth;
                DisplayText("PlacePart_m(): Part down, Z" + Z.ToString(CultureInfo.InvariantCulture), KnownColor.Blue);
                if (!CNC_Z_m(Z))
                {
                    return false;
                }
            }
            DisplayText("PlacePart_m(): Nozzle up.");
            VacuumOff();
            if (!CNC_Z_m(0))  // back up
            {
                return false;
            }
            //ShowMessageBox(
            //    "Debug: Nozzle up.",
            //    "Debug",
            //    MessageBoxButtons.OK);
            return true;
        }

        // =================================================================================
        // 
        private bool PutLoosePartDown_m(bool Probe)
        {
            if (Probe)
            {
                DisplayText("PutLoosePartDown_m(): Probing placement Z");
                if (!Nozzle_ProbeDown_m(Setting.Placement_Placement_Depth))
                {
                    return false;
                }
                LoosePartPlaceZ = Cnc.CurrentZ - Setting.Placement_Placement_Depth;
                DisplayText("PutLoosePartDown_m(): probed Z= " + Cnc.CurrentZ.ToString(CultureInfo.InvariantCulture));
                DisplayText("PutLoosePartDown_m(): placement Z= " + LoosePartPlaceZ.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                if (!CNC_Z_m(LoosePartPlaceZ + Setting.Placement_Placement_Depth))
                {
                    return false;
                }
            }
            VacuumOff();
            DisplayText("PutLoosePartDown_m(): Nozzle up.");
            if (!CNC_Z_m(0))  // back up
            {
                return false;
            }
            return true;
        }

        // ====================================================================================
        // Moves the part a certain distance above the placement position and allows manually
        // fine tuning of the position.
        // After done ENTER will trigger the final placement.
        //
        // MethodeParameter: For Loose Part Assisted => stop distance above board in mm 
        // ====================================================================================
        private bool PutLoosePartDownAssisted_m(string MethodParameter)
        {
            if (!Check_HeightCalibrationDone_m()) return false;

            double distance2pcb;

            // secure convert of string to double
            try
            {
                distance2pcb = Convert.ToDouble(MethodParameter, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                distance2pcb = 2.0; // if convertion faild, set minimum stop distance to board
            }

            // we need a minimum stop distance
            if (distance2pcb < 2.0)
            {
                distance2pcb = 2.0;
            };

            if (!CNC_Z_m(Setting.General_Z0toPCB - distance2pcb))
            {
                return false;
            }

            DisplayText("Now fine tune part position. If done press ENTER");

            // Switch off slack compensation
            // save the present state to restore
            bool SaveSlackCompState = Cnc.SlackCompensation;
            bool SaveSlackCompAState = Cnc.SlackCompensationA;

            Cnc.SlackCompensation = false;
            Cnc.SlackCompensationA = false;

            ZGuardOff(); // Allow nozzle movment while nozzle is down

            // Wait for enter key pressed. 
            EnterKeyHit = false;
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
                if (AbortPlacement)
                {
                    ShowMessageBox(
                        "Operation aborted.",
                        "Operation aborted.",
                        MessageBoxButtons.OK);
                    
                    AbortPlacement = false;                    
                    
                    if (!CNC_Z_m(0))  // move nozzle to zero position
                    {
                        return false;
                    }

                    ZGuardOn();

                    //Restore Slack Compensation state
                    Cnc.SlackCompensation = SaveSlackCompState;
                    Cnc.SlackCompensationA = SaveSlackCompAState;

                    return false;
                }
            } while (!EnterKeyHit);

            // fine tuning part position done, place now part on board
            if (!Nozzle_ProbeDown_m(Setting.Placement_Placement_Depth))
            {
                return false;
            }

            VacuumOff();
            ZGuardOn();

            if (!CNC_Z_m(0))  // move nozzle to zero position
            {
                return false;
            }

            //Restore Slack Compensation states
            Cnc.SlackCompensation = SaveSlackCompState;
            Cnc.SlackCompensationA = SaveSlackCompAState;

            return true;
        }

        // =================================================================================
        // Actual placement 
        // =================================================================================
        private double LoosePartPickupZ = 0.0;
        private double LoosePartPlaceZ = 0.0;

        private bool PickUpLoosePart_m(bool Probe, bool Snapshot, int CADdataRow, int JobDataRow, string Component)
        {
            if (!Check_HeightCalibrationDone_m()) return false;

            DisplayText("PickUpLoosePart_m: " + Probe.ToString(CultureInfo.InvariantCulture) + ", "
                + Snapshot.ToString(CultureInfo.InvariantCulture) + ", "
                + CADdataRow.ToString(CultureInfo.InvariantCulture) + ", "
                + JobDataRow.ToString(CultureInfo.InvariantCulture) + ", "
                + Component, KnownColor.Blue);

            VideoAlgorithmsCollection.FullAlgorithmDescription PartAlg = new VideoAlgorithmsCollection.FullAlgorithmDescription();
            string VidAlgName = JobData_GridView.Rows[JobDataRow].Cells["JobdataMethodParametersColumn"].Value.ToString();
            DisplayText("Using algorithm " + VidAlgName);
            if (!VideoAlgorithms.FindAlgorithm(VidAlgName, out PartAlg))
            {
                DisplayText("*** Fiducial algorithm (" + VidAlgName + ") not found", KnownColor.DarkRed, true);
                return false;
            }
            DownCamera.BuildMeasurementFunctionsList(PartAlg.FunctionList);
            DownCamera.MeasurementParameters = PartAlg.MeasurementParameters;            
            
            // goto pickup
            if (!CNC_XYA_m(Setting.General_PickupCenterX, Setting.General_PickupCenterY, Cnc.CurrentA))
            {
                return false;
            }

            // ask for part 
            string ComponentValue = CadData_GridView.Rows[CADdataRow].Cells["CADdataValueColumn"].Value.ToString();
            string ComponentFootprint = CadData_GridView.Rows[CADdataRow].Cells["CADdataFootprintColumn"].Value.ToString();
            DialogResult dialogResult = ShowMessageBox(
                "Put one " + ComponentValue +", " + ComponentFootprint + " to the pickup location.",
                "Placing " + Component,
                MessageBoxButtons.OKCancel);
            if (dialogResult == DialogResult.Cancel)
            {
                return false;
            }

            // Find component
            double X = 0;
            double Y = 0;
            double A = 0.0;

            bool ok = true;
            do
            {
                ok = true;
                if (!GoToFeatureLocation_m(0.1, out X, out Y, out A))
                {
                    ok = false;
                    string nl = Environment.NewLine;
                    string answer = NonModalMessageBox(
                        "Failed to find the part." + nl +
                        "Jog machine to position and/or tune the algorithm." + nl +
                        "Click \"Retry\" to try again," + nl +
                        "click \"Cancel\" to continue without success", "Part not found",
                        "Retry", "", "Cancel");
                    if (answer == "Cancel")
                    {
                        return false;
                    }
                    DownCamera.BuildMeasurementFunctionsList(PartAlg.FunctionList);
                    DownCamera.MeasurementParameters = PartAlg.MeasurementParameters;
                    Thread.Sleep(100);
                }
            } while (!ok);

            // go exactly on top of component for user confidence and for snapshot to be at right place
            if (Snapshot)
            {
                if (!CNC_XYA_m(Cnc.CurrentX + X, Cnc.CurrentY + Y, Cnc.CurrentA))
                {
                    return false;
                }
                DownCamera.SnapshotRotation = A;
                // xxx DownCamera.BuildMeasurementFunctionsList(DowncamSnapshot_dataGridView);
                Thread.Sleep(200);
                DownCamera.TakeSnapshot();
                DownCamera.Draw_Snapshot = true;
                X = 0.0;
                Y = 0.0;
            };

            if (!Nozzle.Move_m(Cnc.CurrentX + X, Cnc.CurrentY + Y, A))
            {
                return false;
            }

            // pick it up
            if (Probe)
            {
                DisplayText("PickUpLoosePart_m(): Probing pickup Z");
                if (!Nozzle_ProbeDown_m(Setting.Placement_Pickup_Depth))
                {
                    DownCamera.Draw_Snapshot = true;
                    return false;
                }
                LoosePartPickupZ = Cnc.CurrentZ - Setting.Placement_Pickup_Depth;
                DisplayText("PickUpLoosePart_m(): Probed Z= " + Cnc.CurrentZ.ToString(CultureInfo.InvariantCulture));
                DisplayText("PickUpLoosePart_m(): Pickup Z= " + LoosePartPickupZ.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                DisplayText("PickUpLoosePart_m(): Part pickup, Z" + LoosePartPickupZ.ToString(CultureInfo.InvariantCulture));
                if (!CNC_Z_m(LoosePartPickupZ + Setting.Placement_Pickup_Depth))
                {
                    DownCamera.Draw_Snapshot = false;
                    return false;
                }
            }
            VacuumOn();
            DisplayText("PickUpLoosePart_m(): Nozzle up");
            if (!CNC_Z_m(0))
            {
                DownCamera.Draw_Snapshot = true;
                return false;
            }
            if (AbortPlacement)
            {
                if (!AbortPlacementShown)
                {
                    AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                AbortPlacement = false;
                return false;
            }
            return true;
        }

        // ========================================================================================
        // PlacePart_m(): This routine places a single component.
        // Component is at CadData_GridView.Rows[CADdataRow]. 
        // Tape ID and method are at JobDataRow.Rows[JobDataRow]. 
        // It should go to X, Y, A
        // CAD data is validated already.

        private bool PlacePart_m(int CADdataRow, int JobDataRow, double X, double Y, double A, bool FirstInRow, out bool PartAboveCam)
        {
            PartAboveCam = false;
            if (AbortPlacement)
            {
                if (!AbortPlacementShown)
                {
                    AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                AbortPlacement = false;
                return false;
            };
            string id = JobData_GridView.Rows[JobDataRow].Cells["JobdataMethodParametersColumn"].Value.ToString();
            string Method = JobData_GridView.Rows[JobDataRow].Cells["JobdataMethodColumn"].Value.ToString();

            int TapeNum = 0;
            string Component = CadData_GridView.Rows[CADdataRow].Cells["CADdataComponentColumn"].Value.ToString();
            DisplayText("PlacePart_m, Component: " + Component + ", CAD data row: " + CADdataRow.ToString(CultureInfo.InvariantCulture), KnownColor.Blue);

            // Preparing:
            switch (Method)
            {
                case "Place":
                case "Place Assisted":
                case "Place Fast":
                    if (!Tapes.IdValidates_m(id, out TapeNum))
                    {
                        return false;
                    }
                    // First component in a row: We don't necessarily know the correct pickup and placement height, even though there
                    // might be values from previous runs. (PCB and Nozzle might have changed.)
                    if (FirstInRow && MeasureZs_checkBox.Checked)
                    {
                        // Clear heights
                        Tapes_dataGridView.Rows[TapeNum].Cells["Z_Pickup_Column"].Value = "--";
                        Tapes_dataGridView.Rows[TapeNum].Cells["Z_Place_Column"].Value = "--";
                    };
                    break;

                default:
                    // Other methods don't use tapes, nothing to do here.
                    break;
            };

            // Pickup:
            switch (Method)
            {
                case "Place":
                case "Place Assisted":
                    if (!PickUpPartWithHoleMeasurement_m(TapeNum))
                    {
                        return false;
                    }
                    break;

                case "Place Fast":
                    if (!PickUpPartFast_m(TapeNum))
                    {
                        return false;
                    }
                    break;

                case "LoosePart":
                case "LoosePart Assisted":
                    if (!PickUpLoosePart_m(FirstInRow, false, CADdataRow, JobDataRow, Component))
                    {
                        return false;
                    }
                    break;

                case "DownCam Snapshot":
                case "UpCam Snapshot":
                    if (!PickUpLoosePart_m(FirstInRow, true, CADdataRow, JobDataRow, Component))
                    {
                        return false;
                    }
                    break;

                default:
                    ShowMessageBox(
                        "Unknown method at placement time: ",
                        "Operation aborted.",
                        MessageBoxButtons.OK);
                    return false;
                // break;
            };

            /*
            // Take the part to position. With snapshot, we want to fine tune it here:
            if (Method == "UpCam Snapshot")
            {
                DisplayText("PlacePart_m: take Upcam snapshot");
                // Take part to upcam
                if (!CNC_XYA_m(Setting.UpCam_PositionX, Setting.UpCam_PositionY, 0.0))
                {
                    return false;
                };
                if (!CNC_Z_m(LoosePartPickupZ))
                {
                    return false;
                };
                // take snapshot
                SelectCamera(UpCamera);
                // xxx UpCam_TakeSnapshot();
                SelectCamera(DownCamera);
                if (!CNC_Z_m(0.0))
                {
                    DownCamera.Draw_Snapshot = false;
                    return false;
                };
            }

            if ((Method == "DownCam Snapshot") || (Method == "UpCam Snapshot"))
            {
                DisplayText("PlacePart_m: Snapshot place");
                // Take cam to where we think part is going. This might not be exactly right.
                DownCamera.RotateSnapshot(A);
                if (!CNC_XYA_m(X, Y, A))
                {
                    // VacuumOff();  if the above failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                    DownCamera.Draw_Snapshot = false;
                    return false;
                };
                // Wait for enter key press. Before enter, user jogs the part and the image to right place
                EnterKeyHit = false;
                do
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                    if (AbortPlacement)
                    {
                        if (!AbortPlacementShown)
                        {
                            AbortPlacementShown = true;
                            ShowMessageBox(
                                       "Operation aborted",
                                       "Operation aborted",
                                       MessageBoxButtons.OK);
                        }
                        AbortPlacement = false;
                        return false;
                    }
                } while (!EnterKeyHit);
                DownCamera.Draw_Snapshot = false;
                X = Cnc.CurrentX;
                Y = Cnc.CurrentY;
                A = Cnc.CurrentA;
            };
            */

            string PlacementMethod = JobData_GridView.Rows[JobDataRow].Cells["PlacementMethodColumn"].Value.ToString();

            if (PlacementMethod== "--")
            {
                // Take the part to position:
                DisplayText("PlacePart_m: goto placement position");
                if (!Nozzle.Move_m(X, Y, A))
                {
                    // VacuumOff();  if the above failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    return false;
                }

                // Place it:
                if (AbortPlacement)
                {
                    if (!AbortPlacementShown)
                    {
                        AbortPlacementShown = true;
                        ShowMessageBox(
                                   "Operation aborted",
                                   "Operation aborted",
                                   MessageBoxButtons.OK);
                    }
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    AbortPlacement = false;
                    return false;
                }

                switch (Method)
                {
                    case "Place Assisted": // For parts from tapes allows manually correction of part position before placing them
                                           // since for tape parts id contains the Tape index, we use here a fixed stop distance above board
                        if (!PutLoosePartDownAssisted_m("2.5"))
                        {
                            // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                            return false;
                        }
                        break;
                    case "LoosePart Assisted":
                        if (!PutLoosePartDownAssisted_m(id)) // id contains stop distance above board
                        {
                            // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                            return false;
                        }
                        break;
                    case "LoosePart":
                    case "DownCam Snapshot":
                    case "UpCam Snapshot":
                        DownCamera.Draw_Snapshot = false;
                        UpCamera.Draw_Snapshot = false;
                        if (!PutLoosePartDown_m(FirstInRow))
                        {
                            // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                            return false;
                        }
                        break;

                    default:
                        if (!PutPartDown_m(TapeNum))
                        {
                            // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                            DownCamera.Draw_Snapshot = false;
                            UpCamera.Draw_Snapshot = false;
                            return false;
                        }
                        break;
                };
                if (AbortPlacement)
                {
                    if (!AbortPlacementShown)
                    {
                        AbortPlacementShown = true;
                        ShowMessageBox(
                                   "Operation aborted",
                                   "Operation aborted",
                                   MessageBoxButtons.OK);
                    }
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    AbortPlacement = false;
                    return false;
                }
                return true;
            }
            else if (PlacementMethod == "Up Cam Assisted")
            {
                double Xcorr = 0.0;
                double Ycorr = 0.0;
                double Acorr = 0.0;

                // -------------------
                // take part to up cam
                PartAboveCam = true;
                DisplayText("Up Cam Assisted, goto up cam");
                if (!CNC_XYA_m(Setting.UpCam_PositionX, Setting.UpCam_PositionY, A))
                {
                    // VacuumOff();  if the above failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    return false;
                }
                if (!CNC_Z_m(Setting.General_Z0toPCB - 0.5))
                {
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    return false;
                }

                // -------------------
                // measure it
                string PlacementParameter = JobData_GridView.Rows[JobDataRow].Cells["PlacementParameterColumn"].Value.ToString();

                DisplayText("Measuring part, algorithm " + PlacementParameter);
                VideoAlgorithmsCollection.FullAlgorithmDescription Alg = new VideoAlgorithmsCollection.FullAlgorithmDescription();

                if (!VideoAlgorithms.FindAlgorithm(PlacementParameter, out Alg))
                {
                    DisplayText("*** Part placement algorithm " + PlacementParameter + " not found!", KnownColor.Red, true);
                    return false;
                }
                UpCamera.BuildMeasurementFunctionsList(Alg.FunctionList);
                UpCamera.MeasurementParameters = Alg.MeasurementParameters;
                SelectCamera(UpCamera);
                if (!UpCamera.IsRunning())
                {
                    DisplayText("*** Selecting up camera failed", KnownColor.Red, true);
                    return false;
                }

                string nl = Environment.NewLine;
                int tries = 0;
                bool ok = false;
                do
                {
                    for (tries = 0; tries < 8; tries++)
                    {
                        if (UpCamera.Measure(out Xcorr, out Ycorr, out Acorr, false))
                        {
                            ok = true;
                            break;
                        }
                        Thread.Sleep(80); // next frame + vibration damping
                    }
                    if (tries >= 7)
                    {
                        string answer = NonModalMessageBox(
                           " Measuring part position failed." + nl +
                           "Tune the algorithm or fix the issue another way." + nl +
                           "Click \"Retry\" to try again," + nl +
                           "click \"Cancel\" to continue without success", "Part position measurement failed",
                           "Retry", "", "Cancel");
                        if (answer == "Cancel")
                        {
                            return false;
                        }
                    }
                } while (!ok);

                DisplayText("Measurement ok, raw A= " + Acorr.ToString());
                Acorr = -Acorr;     // looking from below...
                Acorr = A - Acorr;
                // regulate to -45 .. 45
                while (Acorr <= -45.0)
                {
                    Acorr = Acorr + 90.0;
                }
                while (Acorr >= 45.0)
                {
                    Acorr = Acorr - 90.0;
                }
                DisplayText("Result: dX= " + Xcorr.ToString() + ", dY= " + Ycorr.ToString() + ", dA= " + Acorr.ToString());
                DisplayText("Target: X= " + X.ToString() + ", Y= " + Y.ToString() + ", A= " + A.ToString());

                // -------------------
                // take part to to X + correction, Y + correction, A+corr
                if (!CNC_Z_m(0))
                {
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    return false;
                }
                X = X + Setting.DownCam_NozzleOffsetX + Xcorr;
                Y = Y + Setting.DownCam_NozzleOffsetY - Ycorr;
                A = Cnc.CurrentA + Acorr;
                if (!CNC_XYA_m(X, Y, A))
                {
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    return false;
                }
                PartAboveCam = false;
                // place part
                if (!PutPartDown_m(TapeNum))
                {
                    DownCamera.Draw_Snapshot = false;
                    UpCamera.Draw_Snapshot = false;
                    return false;
                }
            }
            else
            {
                ShowMessageBox(
                    "Unknown placement method at placement time: ",
                    "Operation aborted.",
                    MessageBoxButtons.OK);
                return false;
            }

            if (AbortPlacement)
            {
                if (!AbortPlacementShown)
                {
                    AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                DownCamera.Draw_Snapshot = false;
                UpCamera.Draw_Snapshot = false;
                AbortPlacement = false;
                return false;
            }
            return true;
        }


        // =================================================================================
        // BuildMachineCoordinateData_m routine builds the machine coordinates data 
        // based on fiducials true (machine coord) location.
        // =================================================================================

        // =================================================================================
        // FindFiducials_m():
        // Finds the fiducials from job data (which by now, exists).
        // Sets FiducialsRow to indicate the row in JobData_GridView

        public bool FindFiducials_m(out int FiducialsRow)
        {
            // I) Find fiducials nominal placement data
            // Ia) Are fiducials indicated and only once?
            bool FiducialsFound = false;
            FiducialsRow = 0;

            for (int i = 0; i < JobData_GridView.RowCount; i++)
            {
                if (JobData_GridView.Rows[i].Cells["JobdataMethodColumn"].Value.ToString() == "Fiducials")
                {
                    if (FiducialsFound)
                    {
                        ShowMessageBox(
                            "Fiducials selected twice in Methods. Please fix!",
                            "Double Fiducials",
                            MessageBoxButtons.OK);
                        return false;
                    }
                    else
                    {
                        FiducialsFound = true;
                        FiducialsRow = i;
                    }
                }
            }
            if (!FiducialsFound)
            {
                // Ib) OriginalFiducials not pointed out yet. Find them automatically.
                // OriginalFiducials designators are FI*** or FID*** where *** is a number.
                string Fids;
                bool FidsOnThisRow = false;
                for (int i = 0; i < JobData_GridView.RowCount; i++)
                {
                    Fids = JobData_GridView.Rows[i].Cells["JobdataComponentsColumn"].Value.ToString();
                    if (Fids.StartsWith("FI", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (Char.IsDigit(Fids[2]))
                        {
                            FidsOnThisRow = true;
                        }
                        else if (Fids.StartsWith("FID", StringComparison.CurrentCultureIgnoreCase))
                        {
                            if (Char.IsDigit(Fids[3]))
                            {
                                FidsOnThisRow = true;
                            }
                        }
                    }
                    if (FidsOnThisRow)
                    {
                        if (FiducialsFound)
                        {
                            ShowMessageBox(
                                "Two group of components starting with FI*. Please indicate Fiducials",
                                "Double Fiducials",
                                MessageBoxButtons.OK);
                            return false;
                        }
                        else
                        {
                            FiducialsFound = true;
                            FiducialsRow = i;
                            FidsOnThisRow = false;
                        }
                    }
                }
                // If fids were found, the row is unique. Mark it:
                if (FiducialsFound)
                {
                    JobData_GridView.Rows[FiducialsRow].Cells["JobdataMethodColumn"].Value = "Fiducials";
                }
            }
            if (!FiducialsFound)
            {
                ShowMessageBox(
                    "Fiducials definitions not found.",
                    "Fiducials not defined",
                    MessageBoxButtons.OK);
                return false;
            }
            return true;
        }

        // =================================================================================
        // MeasureFiducial_m():
        // Takes the parameter nominal location and measures its physical location.
        // Assumes measurement parameters already set.

        private bool MeasureFiducial_m(ref PhysicalComponent fid)
        {
            if (!CNC_XYA_m(fid.X_nominal + Setting.Job_Xoffset + Setting.General_JigOffsetX,
                     fid.Y_nominal + Setting.Job_Yoffset + Setting.General_JigOffsetY, Cnc.CurrentA))
            {
                return false;
            }

            if (!GoToFeatureLocation_m(0.1, out double X, out double Y, out double A))
            {
                ShowMessageBox(
                    "Finding fiducial: Can't regognize fiducial " + fid.Designator,
                    "No Fiducial found",
                    MessageBoxButtons.OK);
                return false;
            }
            fid.X_machine = Cnc.CurrentX + X;
            fid.Y_machine = Cnc.CurrentY + Y;
            // For user confidence, show it:
            for (int i = 0; i < 50; i++)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            return true;
        }

        // =================================================================================
        // ValidateCADdata_m(): Checks, that supplied nominal values are good numbers:
        private bool ValidateCADdata_m()
        {
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                if (!double.TryParse(Row.Cells["CADdataXnominalColumn"].Value.ToString().Replace(',', '.'), out double x))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["CADdataComponentColumn"].Value.ToString() + " X coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                if (!double.TryParse(Row.Cells["CADdataYnominalColumn"].Value.ToString().Replace(',', '.'), out double y))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["CADdataComponentColumn"].Value.ToString() + " Y coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                if (!double.TryParse(Row.Cells["CADdataRotationNominalColumn"].Value.ToString().Replace(',', '.'), out double r))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["CADdataComponentColumn"].Value.ToString() + " rotation data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                // DisplayText(Row.Cells["CADdataComponentColumn"].Value.ToString() + ": x= " + x.ToString() + ", y= " + y.ToString() + ", r= " + r.ToString());
            }
            return true;
        }


        // =================================================================================
        // BuildMachineCoordinateData_m():
        private bool BuildMachineCoordinateData_m()
        {
            double X_nom = 0;
            double Y_nom = 0;
            if (Setting.Placement_SkipMeasurements)
            {
                foreach (DataGridViewRow Row in CadData_GridView.Rows)
                {
                    // Cad data is validated (but still need to test because compiler)
                    if (!double.TryParse(Row.Cells["CADdataXnominalColumn"].Value.ToString().Replace(',', '.'), out X_nom))
                    {
                        DisplayText("Bad X nominal data, component " + Row.Cells["CADdataComponentColumn"].Value.ToString());
                        return false;
                    }
                    if (!double.TryParse(Row.Cells["CADdataYnominalColumn"].Value.ToString().Replace(',', '.'), out Y_nom))
                    {
                        DisplayText("Bad Y nominal data, component " + Row.Cells["CADdataComponentColumn"].Value.ToString());
                        return false;
                    }
                    X_nom += Setting.General_JigOffsetX;
                    Y_nom += Setting.General_JigOffsetY;
                    Row.Cells["CADdataXmachineColumn"].Value = X_nom.ToString("0.000", CultureInfo.InvariantCulture);
                    Row.Cells["CADdataYmachineColumn"].Value = Y_nom.ToString("0.000", CultureInfo.InvariantCulture);

                    Row.Cells["CADdataRotationMachineColumn"].Value = Row.Cells["CADdataRotationNominalColumn"].Value;
                }
                // Refresh UI:
                Update_GridView(CadData_GridView);
                return true;
            };

            if (ValidMeasurement_checkBox.Checked)
            {
                return true;
            }
            // Check, that our data is good:
            if (!ValidateCADdata_m())
                return false;

            // Find the fiducials form CAD data:
            int FiducialsRow = 0;
            if (!FindFiducials_m(out FiducialsRow))
            {
                return false;
            }
            // OriginalFiducials are at JobData_GridView.Rows[FiducialsRow]
            string[] FiducialDesignators = JobData_GridView.Rows[FiducialsRow].Cells["JobdataComponentsColumn"].Value.ToString().Split(',');
            // Are there at least two?
            if (FiducialDesignators.Length < 2)
            {
                ShowMessageBox(
                    "Only one fiducial found.",
                    "Too few fiducials",
                    MessageBoxButtons.OK);
                return false;
            }
            // Get ready for position measurements
            DisplayText("SetFiducialsMeasurement");
            VideoAlgorithmsCollection.FullAlgorithmDescription FidAlg = new VideoAlgorithmsCollection.FullAlgorithmDescription();
            string VidAlgName = JobData_GridView.Rows[FiducialsRow].Cells["JobdataMethodParametersColumn"].Value.ToString();
            if (!VideoAlgorithms.FindAlgorithm(VidAlgName, out FidAlg))
            {
                DisplayText("*** Fiducial algorithm (" + VidAlgName + ") not found", KnownColor.DarkRed, true);
                return false;
            }

            // move them to our array, checking the data:
            PhysicalComponent[] Fiducials = new PhysicalComponent[FiducialDesignators.Length];  // store the data here
            // double X_nom = 0;
            // double Y_nom = 0;
            DownCamera.BuildMeasurementFunctionsList(FidAlg.FunctionList);
            DownCamera.MeasurementParameters = FidAlg.MeasurementParameters;
            for (int i = 0; i < FiducialDesignators.Length; i++)  // for each fiducial in our OriginalFiducials array,
            {
                Fiducials[i] = new PhysicalComponent();
                Fiducials[i].Designator = FiducialDesignators[i];
                // find the fiducial in CAD data.
                foreach (DataGridViewRow Row in CadData_GridView.Rows)
                {
                    if (Row.Cells["CADdataComponentColumn"].Value.ToString() == FiducialDesignators[i]) // If this is the fiducial we want,
                    {
                        // Get its nominal position (value already checked).
                        if (!double.TryParse(Row.Cells["CADdataXnominalColumn"].Value.ToString().Replace(',', '.'), out X_nom))
                        {
                            DisplayText("Bad X nominal data, fiducial " + Row.Cells["CADdataComponentColumn"].Value.ToString());
                            return false;
                        }
                        if (!double.TryParse(Row.Cells["CADdataYnominalColumn"].Value.ToString().Replace(',', '.'), out Y_nom))
                        {
                            DisplayText("Bad Y nominal data, fiducial " + Row.Cells["CADdataComponentColumn"].Value.ToString());
                            return false;
                        }
                        break;
                    }
                }

                if (AbortPlacement)
                {
                    StopOperation();
                    return false;
                }

                // And measure the fiducial's true location:
                Fiducials[i].X_nominal = X_nom;
                Fiducials[i].Y_nominal = Y_nom;
                bool ok = true;
                do
                {
                    ok = true;
                    if (!MeasureFiducial_m(ref Fiducials[i]))
                    {
                        ok = false;
                        string nl = Environment.NewLine;
                        string answer = NonModalMessageBox(
                            "Fiducial measurement failed." + nl +
                            "Jog machine to position and/or tune the algorithm." + nl +
                            "Click \"Retry\" to try again," + nl +
                            "click \"Cancel\" to continue without success", "Fiducial not found",
                            "Retry", "", "Cancel");
                        if (answer == "Cancel")
                        {
                            return false;
                        }
                    }
                } while (!ok);

                if (Setting.Placement_FiducialConfirmation)
                {
                    DialogResult dialogResult = ShowMessageBox(
                        "Fiducial location OK?",
                        "Confirm Fiducial",
                        MessageBoxButtons.OKCancel
                    );
                    if (dialogResult == DialogResult.Cancel)
                    {
                        return false;
                    }
                }
                // We could put the fiducials machine coordinates in place at this point. However, 
                // we don't, since if the algorithms below are correct, the fiducial data will not change more than measurement error.
                // During development, that is a good checkpoint.
            }

            // Find the homographic tranformation from CAD data (fiducials.nominal) to measured machine coordinates
            // (fiducials.machine):
            Transform transform = new Transform();
            int num_corr_points = Fiducials.Length;
            // Special case for 2 fiducials: Inject 3rd bogus fiducal, see below
            if (Fiducials.Length == 2)
            {
                num_corr_points = 3;
            }
            HomographyEstimation.Point[] nominals = new HomographyEstimation.Point[num_corr_points];
            HomographyEstimation.Point[] measured = new HomographyEstimation.Point[num_corr_points];
            // build point data arrays:
            for (int i = 0; i < Fiducials.Length; i++)
            {
                nominals[i].X = Fiducials[i].X_nominal;
                nominals[i].Y = Fiducials[i].Y_nominal;
                nominals[i].W = 1.0;
                measured[i].X = Fiducials[i].X_machine;
                measured[i].Y = Fiducials[i].Y_machine;
                measured[i].W = 1.0;
            }

            // Special case for 2 fiducials: Inject 3rd bogus fiducal
            // The 3rd fiducial is simply the 2nd fiducial rotated +90 degrees around the 1st fiducial
            // This should imply a shear and inversion free transformation, hence collapsing a full affine
            // solution space to a similarity transform
            if (Fiducials.Length == 2)
            {
                double deltaXnominal = nominals[1].X - nominals[0].X;
                double deltaYnominal = nominals[1].Y - nominals[0].Y;
                nominals[2].X = nominals[0].X - deltaYnominal;
                nominals[2].Y = nominals[0].Y + deltaXnominal;
                nominals[2].W = 1.0;

                double deltaXmeasured = measured[1].X - measured[0].X;
                double deltaYmeasured = measured[1].Y - measured[0].Y;
                measured[2].X = measured[0].X - deltaYmeasured;
                measured[2].Y = measured[0].Y + deltaXmeasured;
                measured[2].W = 1.0;
            }

            // find the tranformation
            bool res = transform.Estimate(nominals, measured, ErrorMetric.Transfer, 1450, 1450);  // the PCBs are smaller than 1450mm
            if (!res)
            {
                ShowMessageBox(
                    "Transform estimation failed.",
                    "Data error",
                    MessageBoxButtons.OK);
                return false;
            }
            // Analyze the transform: Displacement is for debug. We could also calculate X & Y stretch and shear, but why bother.
            // Find out the displacement in the transform (where nominal origin ends up):
            HomographyEstimation.Point Loc, Loc2;
            Loc.X = 0.0;
            Loc.Y = 0.0;
            Loc.W = 1.0;
            Loc = transform.TransformPoint(Loc);
            Loc = Loc.NormalizeHomogeneous();
            DisplayText("Transform results:");
            DisplayText("Xorigin= " + (Loc.X).ToString(CultureInfo.InvariantCulture));
            DisplayText("Yorigin= " + Loc.Y.ToString(CultureInfo.InvariantCulture));
            // We do need rotation. Find out by rotatíng a unit vector:
            Loc2.X = 1.0;
            Loc2.Y = 0.0;
            Loc2.W = 1.0;
            Loc2 = transform.TransformPoint(Loc2);
            Loc2 = Loc2.NormalizeHomogeneous();
            // DisplayText("dX= " + Loc2.X.ToString(CultureInfo.InvariantCulture));
            // DisplayText("dY= " + Loc2.Y.ToString(CultureInfo.InvariantCulture));
            double angle = Math.Asin(Loc2.Y - Loc.Y) * 180.0 / Math.PI; // in degrees
            DisplayText("angle= " + angle.ToString(CultureInfo.InvariantCulture));

            // Calculate machine coordinates of all components:
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                // build a point from CAD data values
                Loc.X = 0.0;
                if (double.TryParse(Row.Cells["CADdataXnominalColumn"].Value.ToString().Replace(',', '.'), out double tempD))
                {
                    Loc.X = tempD;
                }
                Loc.Y = 0.0;
                if (double.TryParse(Row.Cells["CADdataYnominalColumn"].Value.ToString().Replace(',', '.'), out tempD))
                {
                    Loc.Y = tempD;
                }
                Loc.W = 1;
                // transform it
                Loc = transform.TransformPoint(Loc);
                Loc = Loc.NormalizeHomogeneous();
                // store calculated location values
                Row.Cells["CADdataXmachineColumn"].Value = Loc.X.ToString("0.000", CultureInfo.InvariantCulture);
                Row.Cells["CADdataYmachineColumn"].Value = Loc.Y.ToString("0.000", CultureInfo.InvariantCulture);
                // handle rotation
                double rot = 0.0;
                if (double.TryParse(Row.Cells["CADdataRotationNominalColumn"].Value.ToString().Replace(',', '.'), out rot))
                {
                    rot += angle;
                }
                NormalizeRotation(ref rot);
                Row.Cells["CADdataRotationMachineColumn"].Value = rot.ToString("0.0000", CultureInfo.InvariantCulture);

            }
            // Refresh UI:
            Update_GridView(CadData_GridView);

            // For debug, compare fiducials true measured locations and the locations.
            // Also, if a fiducials moves more than 0.5mm, something is off (maybe there
            // was a via too close to a fid, and we picked that for a measurement). Warn the user!
            bool DataOk = true;
            double dx, dy;
            for (int i = 0; i < Fiducials.Length; i++)
            {
                Loc.X = Fiducials[i].X_nominal;
                Loc.Y = Fiducials[i].Y_nominal;
                Loc.W = 1.0;
                Loc = transform.TransformPoint(Loc);
                Loc = Loc.NormalizeHomogeneous();
                dx = Math.Abs(Loc.X - Fiducials[i].X_machine);
                dy = Math.Abs(Loc.Y - Fiducials[i].Y_machine);
                DisplayText(Fiducials[i].Designator +
                    ": x_meas= " + Fiducials[i].X_machine.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", x_calc= " + Loc.X.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", dx= " + dx.ToString("0.000", CultureInfo.InvariantCulture) +
                    ": y_meas= " + Fiducials[i].Y_machine.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", y_calc= " + Loc.Y.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", dy= " + dy.ToString("0.000", CultureInfo.InvariantCulture));
                if ((Math.Abs(dx) > 0.4) || (Math.Abs(dy) > 0.4))
                {
                    DataOk = false;
                }
            };
            if (!DataOk)
            {
                DisplayText(" ** A fiducial moved more than 0.4mm from its measured location");
                DisplayText(" ** when applied the same calculations than regular components.");
                DisplayText(" ** (Maybe the camera picked a via instead of a fiducial?)");
                DisplayText(" ** Placement data is likely not good.");
                DialogResult dialogResult = ShowMessageBox(
                    "Nominal to machine trasnformation seems to be off. (See log window)",
                    "Cancel operation?",
                    MessageBoxButtons.OKCancel
                );
                if (dialogResult == DialogResult.Cancel)
                {
                    return false;
                }
            }
            // Done! 
            ValidMeasurement_checkBox.Checked = true;
            return true;
        }// end BuildMachineCoordinateData_m

        // =================================================================================
        // BuildMachineCoordinateData_m functions end
        // =================================================================================


        // =================================================================================
        private void PausePlacement_button_Click(object sender, EventArgs e)
        {
            DialogResult dialogResult = ShowMessageBox(
                "Placement Paused. Continue? (Cancel aborts)",
                "Placement Paused",
                MessageBoxButtons.OKCancel
            );
            if (dialogResult == DialogResult.Cancel)
            {
                AbortPlacement = true;
                AbortPlacementShown = true;
            }

        }

        private void ManualNozzleChange_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            ChangeNozzleManually_m();
        }


        // =================================================================================
        private void StopPlacement_button_Click(object sender, EventArgs e)
        {
            AbortPlacement = true;
            AbortPlacementShown = false;
        }

        // =================================================================================
        private void JobOffsetX_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JobOffsetX_textBox.Text.Replace(',', '.'), out val))
            {
                JobOffsetX_textBox.ForeColor = Color.Black;
                Setting.Job_Xoffset = val;
            }
            else
            {
                JobOffsetX_textBox.ForeColor = Color.Red;
            }
        }

        // =================================================================================
        private void JobOffsetY_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JobOffsetY_textBox.Text.Replace(',', '.'), out val))
            {
                JobOffsetY_textBox.ForeColor = Color.Black;
                Setting.Job_Yoffset = val;
            }
            else
            {
                JobOffsetY_textBox.ForeColor = Color.Red;
            }
        }

        // =================================================================================
        private void ShowNominal_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            double X;
            double Y;
            double A;

            DataGridViewCell cell = CadData_GridView.CurrentCell;
            if (cell == null)
            {
                return;  // no component selected
            }
            if (cell.OwningRow.Index < 0)
            {
                return;  // header row
            }
            if (!double.TryParse(cell.OwningRow.Cells["CADdataXnominalColumn"].Value.ToString().Replace(',', '.'), out X))
            {
                ShowMessageBox(
                    "Bad data at X_nominal",
                    "Bad data",
                    MessageBoxButtons.OK);
                return;
            }

            if (!double.TryParse(cell.OwningRow.Cells["CADdataYnominalColumn"].Value.ToString().Replace(',', '.'), out Y))
            {
                ShowMessageBox(
                    "Bad data at Y_nominal",
                    "Bad data",
                    MessageBoxButtons.OK);
                return;
            }

            if (!double.TryParse(cell.OwningRow.Cells["CADdataRotationNominalColumn"].Value.ToString().Replace(',', '.'), out A))
            {
                ShowMessageBox(
                    "Bad data at Rotation",
                    "Bad data",
                    MessageBoxButtons.OK);
                return;
            }

            CNC_XYA_m(X + Setting.Job_Xoffset + Setting.General_JigOffsetX,
                Y + Setting.Job_Yoffset + Setting.General_JigOffsetY, Cnc.CurrentA);
            DownCamera.ArrowAngle = A;
            DownCamera.DrawArrow = true;

            //ShowMessageBox(
            //    "This is " + cell.OwningRow.Cells["CADdataComponentColumn"].Value.ToString() + " approximate (nominal) location",
            //    "Locate Component",
            //    MessageBoxButtons.OK);
        }

        private void ShowNominal_button_Leave(object sender, EventArgs e)
        {
            DownCamera.DrawArrow = false;
        }


        // =================================================================================
        // Checks what is needed to check before doing something for a single component selected at "CAD data" table. 
        // If succesful, sets X, Y to component machine coordinates.
        private bool PrepareSingleComponentOperation(out double X, out double Y)
        {
            X = 0.0;
            Y = 0.0;

            DataGridViewCell cell = CadData_GridView.CurrentCell;
            if (cell == null)
            {
                return false;  // no component selected
            }
            if (cell.OwningRow.Index < 0)
            {
                return false;  // header row
            }
            if (cell.OwningRow.Cells["CADdataXmachineColumn"].Value.ToString() == "Nan")
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Component locations not yet measured. Measure now?",
                    "Measure now?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    return false;
                }
                if (!BuildMachineCoordinateData_m())
                {
                    return false;
                }
            }

            if (!double.TryParse(cell.OwningRow.Cells["CADdataXmachineColumn"].Value.ToString().Replace(',', '.'), out X))
            {
                ShowMessageBox(
                    "Bad data at X_machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            }

            if (!double.TryParse(cell.OwningRow.Cells["CADdataYmachineColumn"].Value.ToString().Replace(',', '.'), out Y))
            {
                ShowMessageBox(
                    "Bad data at Y_machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            }
            return true;
        }

        // =================================================================================
        private void ShowMachine_button_Click(object sender, EventArgs e)
        {
            double X;
            double Y;
            double A;

            if (!CheckPositionConfidence()) return;

            if (!PrepareSingleComponentOperation(out X, out Y))
            {
                return;
            }
            CNC_XYA_m(X, Y, Cnc.CurrentA);
            if (!double.TryParse(CadData_GridView.CurrentCell.OwningRow.Cells["CADdataRotationMachineColumn"].Value.ToString().Replace(',', '.'), out A))
            {
                ShowMessageBox(
                    "Bad data at Rotation_machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return;
            }
            DownCamera.ArrowAngle = A;
            DownCamera.DrawArrow = true;

            //bool KnownComponent = ShowFootPrint_m(cell.OwningRow.Index);
            //ShowMessageBox(
            //    "This is " + cell.OwningRow.Cells["CADdataComponentColumn"].Value.ToString() + " location",
            //    "Locate Component",
            //    MessageBoxButtons.OK);
            //if (KnownComponent)
            //{
            //    DownCamera.DrawBox = false;
            //}
        }

        private void ShowMachine_button_Leave(object sender, EventArgs e)
        {
            DownCamera.DrawArrow = false;
        }


        // =================================================================================
        private void ReMeasure_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            ValidMeasurement_checkBox.Checked = false;
            ValidMeasurement_checkBox.Checked = BuildMachineCoordinateData_m();
            // CNC_Park();
        }

        private void TestNozzleRecognition_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            double X = Cnc.CurrentX;
            double Y = Cnc.CurrentY;
            CalibrateNozzle_m();
            CNC_XYA_m(X, Y, 0.0);
        }


        // =================================================================================
        private void Demo_button_Click(object sender, EventArgs e)
        {
            DemoThread = new Thread(() => DemoWork());
            DemoRunning = true;
            CNC_Z_m(0.0);
            DemoThread.IsBackground = true;
            DemoThread.Start();
        }

        private void StopDemo_button_Click(object sender, EventArgs e)
        {
            DemoRunning = false;
        }

        private bool DemoRunning = false;
        private Thread DemoThread;

        private void DemoWork()
        {

            while (DemoRunning)
            {

            }
        }


        // =================================================================================
        // Panelizing
        // =================================================================================
        private void Panelize_button_Click(object sender, EventArgs e)
        {
            PanelizeForm PanelizeDialog = new PanelizeForm(this);
            PanelizeDialog.CadData = CadData_GridView;
            PanelizeDialog.JobData = JobData_GridView;
            PanelizeDialog.StartPosition = FormStartPosition.CenterParent;
            PanelizeDialog.ShowDialog(this);
            if (PanelizeDialog.OK == true)
            {
                DisplayText("Panelize ok.");
                Update_GridView(CadData_GridView);
                Update_GridView(JobData_GridView);
            }
        }

        // =================================================================================

        private void OmitNozzleCalibration_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_OmitNozzleCalibration = OmitNozzleCalibration_checkBox.Checked;

        }

        private void SkipMeasurements_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_SkipMeasurements = SkipMeasurements_checkBox.Checked;
        }


        #endregion Job page functions

        // =================================================================================
        //CAD data reading functions: Tries to understand different pick and place file formats
        // =================================================================================
        #region CAD data reading functions

        // =================================================================================
        // CADdataToMMs_m(): Data was in inches, convert to mms

        private bool CADdataToMMs_m()
        {
            double val;
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                if (!double.TryParse(Row.Cells["CADdataXnominalColumn"].Value.ToString().Replace(',', '.'), out val))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["CADdataComponentColumn"].Value.ToString() + " X coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                Row.Cells["CADdataXnominalColumn"].Value = Math.Round((val * 25.4), 3).ToString(CultureInfo.InvariantCulture);
                if (!double.TryParse(Row.Cells["CADdataYnominalColumn"].Value.ToString().Replace(',', '.'), out val))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["CADdataComponentColumn"].Value.ToString() + " Y coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                Row.Cells["CADdataYnominalColumn"].Value = Math.Round((val * 25.4), 3).ToString(CultureInfo.InvariantCulture);
            }
            return true;
        }

        // =================================================================================
        // ParseKiCadData_m()
        // =================================================================================
        private bool ParseKiCadData_m(String[] AllLines)
        {
            // Convert KiCad data to regular CSV
            int i = 0;
            bool inches = false;
            // Skip headers until find one starting with "## "
            while (!(AllLines[i].StartsWith("## ", StringComparison.Ordinal)))
            {
                i++;
            };

            // inches vs mms
            if (AllLines[i++].Contains("inches"))  
            {
                inches = true;
            }
            i++; // skip the "Side" line
            List<string> KiCadLines = new List<string>();
            KiCadLines.Add(AllLines[i++].Substring(2));  // add header, skip the "# " start
            // add rest of the lines
            while (!(AllLines[i].StartsWith("## ", StringComparison.Ordinal)))
            {
                KiCadLines.Add(AllLines[i++]);
            };
            // parse the data
            string[] KicadArr = KiCadLines.ToArray();
            if (!ParseCadData_m(KicadArr, true))
            {
                return false;
            };
            // convert to mm'f if needed
            if (inches)
            {
                return (CADdataToMMs_m());
            }
            else
            {
                return true;
            }
        }

        // =================================================================================
        // ParseCadData_m(): main function called from file open
        // =================================================================================

        // =================================================================================
        // FindDelimiter_m(): Tries to find the difference with comma and semicolon separated files  
        bool FindDelimiter_m(String Line, out char delimiter)
        {
            int commas = 0;
            foreach (char c in Line)
            {
                if (c == ',')
                {
                    commas++;
                }
            };
            int semicolons = 0;
            foreach (char c in Line)
            {
                if (c == ';')
                {
                    semicolons++;
                }
            };
            if ((commas == 0) && (semicolons > 4))
            {
                delimiter = ';';
                return true;
            };
            if ((semicolons == 0) && (commas > 4))
            {
                delimiter = ',';
                return true;
            };

            ShowMessageBox(
                "FileName header parse fail",
                "Data format error",
                MessageBoxButtons.OK
            );
            delimiter = ',';
            return false;
        }

        private bool ParseCadData_m(String[] AllLines, bool KiCad)
        {
            int PlacedIndex;
            bool PlacedDataPresent;
            int ComponentIndex;
            int ValueIndex;
            int FootPrintIndex;
            int X_Nominal_Index;
            int Y_Nominal_Index;
            int RotationIndex;
            int LayerIndex = -1;
            bool LayerDataPresent = false;
            int LineIndex = 0;
            int i;

            // Parse header. 
            string FirstLine = AllLines[0];
            if(FirstLine== "Altium Designer Pick and Place Locations")
            {
                // Altium17 file
                for (int ind = 0; ind < 11; ind++)
                {
                    AllLines[ind] = "";   // so these will get skipped in next step
                }
            }

            foreach (string s in AllLines)
            {
                // Skip empty lines and comment lines(starting with # or "//")
                if (string.IsNullOrEmpty(s))
                {
                    LineIndex++;
                    continue;
                }
                if (s[0] == '#')
                {
                    LineIndex++;
                    continue;
                };
                if ((s.Length > 1) && (s[0] == '/') && (s[1] == '/'))
                {
                    LineIndex++;
                    continue;
                };
                break;
            };

            char delimiter;
            if (KiCad)
            {
                delimiter = ' ';
            }
            else
            {
                if (!FindDelimiter_m(AllLines[LineIndex], out delimiter))
                {
                    return false;
                };
            }

            List<String> Headers;
            bool quit = false;
            if (KiCad)
            {
                Headers = SplitKiCadLine(AllLines[LineIndex++]);
            }
            else
            {
                Headers = SplitCSV(AllLines[LineIndex++], delimiter, out quit);
                if (quit)
                {
                    return false;
                }
            }

            for (i = 0; i < Headers.Count; i++)
            {
                if (Headers[i] == "Placed")
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                PlacedDataPresent= false;
            }
            else
            {
                PlacedDataPresent= true;
            }
            PlacedIndex = i;

            List<string> DesignatorList = new List<string> { "designator", "part", "ref", "refdes", "component" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (DesignatorList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component/Designator/Name not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            ComponentIndex = i;

            List<string> ValueList = new List<string> { "value", "val", "comment"};
            for (i = 0; i < Headers.Count; i++)
            {
                if (ValueList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component value/comment not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            ValueIndex = i;

            List<string> PackageList = new List<string> { "footprint", "package", "pattern" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (PackageList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component footprint/pattern not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            FootPrintIndex = i;

            List<string> XList = new List<string> { "x", "x (mm)", "Center-X(mm)", "PosX", "mid x" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (XList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component X not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            X_Nominal_Index = i;

            List<string> YList = new List<string> { "y", "y (mm)", "Center-y(mm)", "Posy", "mid y" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (YList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component Y not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            Y_Nominal_Index = i;

            List<string> RotationList = new List<string> { "rotation", "rot", "rotate" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (RotationList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component rotation not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            RotationIndex = i;

            List<string> LayerList = new List<string> { "layer", "side", "tb" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (LayerList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    LayerIndex = i;
                    LayerDataPresent = true;
                    break;
                }
            }

            // clear and rebuild the data tables
            CadData_GridView.Rows.Clear();
            Update_GridView(CadData_GridView);
            CadDataDelay_label.Text = "Loading...";
            CadDataDelay_label.Visible = true;
            this.Refresh();

            JobData_GridView.Rows.Clear();
            Update_GridView(JobData_GridView);

            foreach (DataGridViewColumn column in JobData_GridView.Columns)
            {
                if (column.HeaderText!="Nozzle")
                {
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;   // disable manual sort
                }
            }

            // Parse data
            List<String> NewLine;
            List<List<string>> DataLines = new List<List<string>>();
            for (i = LineIndex; i < AllLines.Count(); i++)   // for each component
            {
                // Skip: empty lines and comment lines (starting with # or "//")
                if (
                        (string.IsNullOrEmpty(AllLines[i]))  // empty lines
                        ||
                        (AllLines[i] == "\"\"")  // empty lines ("")
                        ||
                        (AllLines[i][0] == '#')  // comment lines starting with #
                        ||
                        ((AllLines[i].Length > 1) && (AllLines[i][0] == '/') && (AllLines[i][1] == '/'))  // // comment lines starting with //
                    )
                {
                    continue;
                }

                if (KiCad)
                {
                    NewLine = SplitKiCadLine(AllLines[i]);
                }
                else
                {
                    NewLine = SplitCSV(AllLines[i], delimiter, out quit);
                    if (quit)
                    {
                        return false;
                    }
                }
                DataLines.Add(NewLine);
            }

            // DataLines now contain the splitted data from the original CSV file, with header and empty lines removed.

            HandleDuplicates(ref DataLines, ComponentIndex);    

            foreach (var Line in DataLines)
            {

                // Line = SplitCSV(AllLines[i], delimiter);
                // If layer is indicated and the component is not on this layer, skip it
                // TODO: Notify user if component is not on either layer (unknown data), once only and continue.
                // TODO: Fix bug: If component is not on either layer, skip it.
                // TODO: Use list of strings for layers and other fields, add “TopLayer” and “BottomLayer” for AD17.

                if (LayerDataPresent)
                {
                    List<string> TopList = new List<string> { "top", "t", "F.Cu" };
                    List<string> BottomList = new List<string> { "bottom", "b", "B.Cu", "bot" };
                    if (Bottom_checkBox.Checked)
                    {
                        if (TopList.Contains(Line[LayerIndex], StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (BottomList.Contains(Line[LayerIndex], StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                }
                CadData_GridView.Rows.Add();
                int Last = CadData_GridView.RowCount - 1;
                CadData_GridView.Rows[Last].Cells["CADdataComponentColumn"].Value = Line[ComponentIndex];
                CadData_GridView.Rows[Last].Cells["CADdataValueColumn"].Value = Line[ValueIndex];
                CadData_GridView.Rows[Last].Cells["CADdataFootprintColumn"].Value = Line[FootPrintIndex];
                CadData_GridView.Rows[Last].Cells["CADdataRotationNominalColumn"].Value = Line[RotationIndex];

                if (PlacedDataPresent)
	            {
                    if ((Line[PlacedIndex]=="True")||(Line[PlacedIndex]=="true"))
                    {
                        CadData_GridView.Rows[Last].Cells["CADdataPlacedColumn"].Value = true;
                    }
		            else
	                {
                        CadData_GridView.Rows[Last].Cells["CADdataPlacedColumn"].Value = false;
	                }
	            }
		        else
	            {
                    CadData_GridView.Rows[Last].Cells["CADdataPlacedColumn"].Value = false;
	            }

                if (LayerDataPresent)
                {
                    if (Bottom_checkBox.Checked)
                    {
                        if (Line[X_Nominal_Index].StartsWith("-", StringComparison.Ordinal))
                        {
                            CadData_GridView.Rows[Last].Cells["CADdataXnominalColumn"].Value = Line[X_Nominal_Index].Replace("mm", "").Replace("-", "");
                        }
                        else
                        {
                            CadData_GridView.Rows[Last].Cells["CADdataXnominalColumn"].Value = "-" + Line[X_Nominal_Index].Replace("mm", "");
                        }
                        double rot;
                        if (!double.TryParse(CadData_GridView.Rows[Last].Cells["CADdataRotationNominalColumn"].Value.ToString().Replace(',', '.'), out rot))
                        {
                            ShowMessageBox(
                                "Bad data at Rotation",
                                "Bad data",
                                MessageBoxButtons.OK);
                            return false;
                        }
                        rot = -rot + 180;
                        CadData_GridView.Rows[Last].Cells["CADdataRotationNominalColumn"].Value = rot.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        CadData_GridView.Rows[Last].Cells["CADdataXnominalColumn"].Value = Line[X_Nominal_Index].Replace("mm", "");
                    }
                }
                else
                {
                    CadData_GridView.Rows[Last].Cells["CADdataXnominalColumn"].Value = Line[X_Nominal_Index].Replace("mm", "");
                }
                CadData_GridView.Rows[Last].Cells["CADdataYnominalColumn"].Value = Line[Y_Nominal_Index].Replace("mm", "");
                CadData_GridView.Rows[Last].Cells["CADdataXnominalColumn"].Value = CadData_GridView.Rows[Last].Cells["CADdataXnominalColumn"].Value.ToString().Replace(",", ".");
                CadData_GridView.Rows[Last].Cells["CADdataYnominalColumn"].Value = CadData_GridView.Rows[Last].Cells["CADdataYnominalColumn"].Value.ToString().Replace(",", ".");
                CadData_GridView.Rows[Last].Cells["CADdataXmachineColumn"].Value = "Nan";   // will be set later 
                CadData_GridView.Rows[Last].Cells["CADdataYmachineColumn"].Value = "Nan";
                CadData_GridView.Rows[Last].Cells["CADdataRotationMachineColumn"].Value = "Nan";
            }   // end "for each component..."

            // Disable manual sorting
            foreach (DataGridViewColumn column in CadData_GridView.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            CadData_GridView.ClearSelection();
            // Check, that our data is good:
            bool result = ValidateCADdata_m();
            CadDataDelay_label.Visible = false;
            Update_GridView(CadData_GridView);
            this.Refresh();
            return result;
        }   // end ParseCadData

        // =================================================================================
        // HandleDuplicates():
        // If the inout data has duplicate designators, like R1, R1, R1, this routine 
        // replaces them with R1_1, R1_2, R1_3 etc.

        public void HandleDuplicates(ref List<List<string>> DataLines, int ComponentIndex)
        {
            string Designator = "";
            int Count;
            bool DuplicateFound;

            // For each component,
            for (int i = 0; i < DataLines.Count; i++)
            {
                Designator = DataLines[i][ComponentIndex];  // get the designator
                DuplicateFound = false;
                Count = 2;
                // and check, if there are duplicates
                for (int j = i+1; j < DataLines.Count; j++)
                {
                    if (DataLines[j][ComponentIndex]==Designator)
                    {
                        DuplicateFound = true;
                        DataLines[j][ComponentIndex] = Designator + "_" + Count.ToString(CultureInfo.InvariantCulture);
                        Count++;
                    }
                }
                // if there were, all others are now renamed but the first one
                if (DuplicateFound)
                {
                    DataLines[i][ComponentIndex] = Designator + "_1";
                }
            }
        }


        // =================================================================================
        // Helper function for ParseCadData() (and some others, hence public)

        public List<String> SplitCSV(string InputLine, char delimiter, out bool quit)
        {
            // input lines can be "xxx","xxxx","xx"; output is array: xxx  xxxxx  xx
            // or xxx,xxxx,xx; output is array: xxx  xxxx  xx
            // or xxx,"xx,xx",xxxx; output is array: xxx  xx,xx  xxxx

            quit = false;
            List<String> Tokens = new List<string>();
            string Line = InputLine;
            DialogResult dialogResult;
            while (!string.IsNullOrEmpty(Line))
            {
                // skip the delimiter(s)
                while (Line[0] == delimiter)
                {
                    if (Line.Length < 2)
                    {
                        dialogResult = ShowMessageBox(
                           "Unexpected end of line on " + InputLine,
                           "Line parsing failed",
                            MessageBoxButtons.OKCancel);
                        if (dialogResult == DialogResult.Cancel)
                        {
                            quit = true;
                            return (Tokens);
                        }
                    }
                    dialogResult = ShowMessageBox(
                       "Warning: empty field on line " + InputLine,
                       "Empty field",
                            MessageBoxButtons.OKCancel);
                    if (dialogResult == DialogResult.Cancel)
                    {
                        quit = true;
                        return (Tokens);
                    }
                    Tokens.Add("");
                    Line = Line.Substring(1);
                };
                // add token
                if (Line[0] == '"')
                {
                    if (Line.Length < 2)
                    {
                        dialogResult = ShowMessageBox(
                           "Unexpected end of line on " + InputLine,
                           "Line parsing failed",
                            MessageBoxButtons.OKCancel);
                        if (dialogResult == DialogResult.Cancel)
                        {
                            quit = true;
                            return (Tokens);
                        }
                    }
                    // token is "xxx"
                    Line = Line.Substring(1);   // skip the first "
                    Tokens.Add(Line.Substring(0, Line.IndexOf('"')));
                    if ( (Line.IndexOf('"') + 1) == Line.Length)
                    {
                        Line = "";
                    }
                    else
                    {
                        Line = Line.Substring(Line.IndexOf('"') + 2);  // skip the " and the delimiter
                    }
                }
                else
                {
                    // token does not have "" 's
                    if (Line.IndexOf(delimiter) < 0)
                    {
                        Tokens.Add(Line);   // last element
                        Line = "";
                    }
                    else
                    {
                        Tokens.Add(Line.Substring(0, Line.IndexOf(delimiter)));
                        Line = Line.Substring(Line.IndexOf(delimiter)+1);
                    }
                }
            }
            // remove leading spaces
            for (int i = 0; i < Tokens.Count; i++)
            {
                Tokens[i] = Tokens[i].Trim();
            }
            return Tokens;
        }

        private List<String> SplitKiCadLine(string InputLine)
        {
            // input line is space formatted:
            // C123     0,1uF/50V        SM0603              1.6025     2.0964     180.0    top
            // tokens may have "" around them

            List<String> Tokens = new List<string>();
            string Line = InputLine;
            while (!string.IsNullOrEmpty(Line))
            {
                // skip leading spaces
                Line = Line.Trim(' ');       

                // add token
                if (Line[0] == '"')
                {
                    if (Line.Length < 2)
                    {
                        ShowMessageBox(
                           "Unexpected end of line on " + InputLine,
                           "Line parsing failed",
                           MessageBoxButtons.OK);
                        return (Tokens);
                    }
                    // token is "xxx"
                    Line = Line.Substring(1);   // skip the first "
                    Tokens.Add(Line.Substring(0, Line.IndexOf('"')));
                    if ((Line.IndexOf('"') + 1) == Line.Length)
                    {
                        Line = "";
                    }
                    else
                    {
                        Line = Line.Substring(Line.IndexOf('"') + 2);  // skip the " and the delimiter
                    }
                }
                else
                {
                    // token does not have "" 's
                    if (Line.IndexOf(' ') < 0)
                    {
                        Tokens.Add(Line);   // last element
                        Line = "";
                    }
                    else
                    {
                        Tokens.Add(Line.Substring(0, Line.IndexOf(' ')));
                        Line = Line.Substring(Line.IndexOf(' ') + 1);
                    }
                }
            }
            // remove leading spaces
            return (Tokens);
        }

        #endregion  CAD data reading functions

        // =================================================================================
        // Tape Positions page functions
        // =================================================================================
        #region Tape Positions page functions

        // This is set up at Form1_Load()
        void Tapes_dataGridView_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // Ugly, but MS raises this error when programmatically changing a combobox cell value
        }

        private void Tapes_tabPage_Begin()
        {
            DisplayText("Setup Tapes tab begin");
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                row.HeaderCell.Value = row.Index.ToString(CultureInfo.InvariantCulture);
                row.Cells["SelectButton_Column"].Value = "Reset";
            }
            SetDownCameraDefaults();
            SelectCamera(DownCamera);
            TapeSetupZguard_checkBox.Checked = false;
        }

        private void Tapes_tabPage_End()
        {
            ZGuardOn();
        }

        // =================================================================================
        public void TapeWidthStringToValues(string WidthStr, out double Xoff, out double Yoff, out double pitch)
        {
            switch (WidthStr)
            {
                case "8/2mm":
                    pitch = 2.0;
                    Xoff = 3.5;
                    Yoff = 2.0;
                    break;
                case "8/4mm":
                    pitch = 4.0;
                    Xoff = 3.50;
                    Yoff = 2.0;
                    break;
                case "12/4mm":
                    pitch = 4.0;
                    Xoff = 5.50;
                    Yoff = 2.0;
                    break;
                case "12/8mm":
                    pitch = 8.0;
                    Xoff = 5.50;
                    Yoff = 2.0;
                    break;
                case "16/4mm":
                    pitch = 4.0;
                    Xoff = 7.50;
                    Yoff = 2.0;
                    break;
                case "16/8mm":
                    pitch = 8.0;
                    Xoff = 7.50;
                    Yoff = 2.0;
                    break;
                case "16/12mm":
                    pitch = 12.0;
                    Xoff = 7.50;
                    Yoff = 2.0;
                    break;
                case "24/4mm":
                    pitch = 4.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/8mm":
                    pitch = 8.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/12mm":
                    pitch = 12.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/16mm":
                    pitch = 16.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/20mm":
                    pitch = 20.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "32/4mm":
                    pitch = 4.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/8mm":
                    pitch = 8.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/12mm":
                    pitch = 12.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/16mm":
                    pitch = 16.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/20mm":
                    pitch = 20.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/24mm":
                    pitch = 24.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/28mm":
                    pitch = 28.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/32mm":
                    pitch = 32.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                default:
                    pitch = 0.0;
                    Xoff = 0.0;
                    Yoff = 2.0;
                    break;
            }
        }

        // =================================================================================
        // tape parameters edit dialog

        private int TapesGridEditRow = 0;
        private int TapesGridEnterRow = 0;

        private void Tapes_dataGridView_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            TapesGridEnterRow = e.RowIndex;
        }

        private void Tapes_dataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                TapesGridEditRow = TapesGridEnterRow;
            }
        }

        private void Invoke_TapeEditDialog(int row, bool CreatingNew)
        {
            DisplayText("Open edit tape dialog", KnownColor.DarkGreen, true);
            TapeEditForm TapeEditDialog = new TapeEditForm(Cnc, DownCamera);
            TapeEditDialog.MainForm = this;
            TapeEditDialog.TapeRowNo = row;
            TapeEditDialog.TapesDataGrid = Tapes_dataGridView;
            TapeEditDialog.Row = Tapes_dataGridView.Rows[row];
            TapeEditDialog.CreatingNew = CreatingNew;
            AttachHelpHandlers(TapeEditDialog.Controls);
            // TapeEditDialog.Parent = this;
            TapeEditDialog.StartPosition = FormStartPosition.CenterParent;
            TapeEditDialog.ShowDialog(this);
        }

        private void EditTape_button_Click(object sender, EventArgs e)
        {
            if (Tapes_dataGridView.SelectedCells.Count != 1)
            {
                DisplayText("Nothing selected");
                return;
            };
            Invoke_TapeEditDialog(Tapes_dataGridView.CurrentCell.RowIndex, false);
        }

        private void EditTape_MenuItemClick(object sender, EventArgs e)
        {
            if (TapesGridEditRow < 0)
            {
                return; // user clicked header or empty space
            }
            Invoke_TapeEditDialog(TapesGridEditRow, false);
        }

        // end edit dialog stuff
        // =================================================================================


        private void AddTape_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            DataGridViewSelectedRowCollection SelectedRows = Tapes_dataGridView.SelectedRows;
            int index = 0;
            if (SelectedRows.Count == 0)
            {
                // add to the end
                Tapes_dataGridView.Rows.Insert(Tapes_dataGridView.Rows.Count);
                index = Tapes_dataGridView.Rows.Count - 1;
            }
            else
            {
                // replace current
                index = Tapes_dataGridView.SelectedRows[0].Index;
                Tapes_dataGridView.Rows.RemoveAt(index);
                Tapes_dataGridView.Rows.Insert(index);
            };
            // Add data
            // SelectButton_Column: On main form, resets tape to position 1.
            // The gridView is moved to selection dialog on job run time. There the SelectButton selects that tape.
            Tapes_dataGridView.Rows[index].Cells["SelectButton_Column"].Value = "Reset";
            // Id_Column: User settable name for the tape
            Tapes_dataGridView.Rows[index].Cells["Id_Column"].Value = index.ToString(CultureInfo.InvariantCulture);
            // FirstX_Column, FirstY_Column: Originally set approximate location for the first hole
            Tapes_dataGridView.Rows[index].Cells["FirstX_Column"].Value = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["FirstY_Column"].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            // Orientation_Column: Which way the tape is set. It is the direction to go for next part
            Tapes_dataGridView.Rows[index].Cells["Orientation_Column"].Value = "+X";
            // Rotation_Column: Which way the parts are rotated on the tape. if 0, parts form +Y oriented tape
            // correspont to 0deg. on the PCB, tape.e. the placement operation does not rotate them.
            Tapes_dataGridView.Rows[index].Cells["Rotation_Column"].Value = "0deg.";
            // WidthColumn: sets the width of the tape and the distance from one part to next. 
            // From EIA-481, we get the part location from the hole location.
            Tapes_dataGridView.Rows[index].Cells["Width_Column"].Value = "8/4mm";
            // Type_Column: used in hole/part recognition
            DataGridViewComboBoxCell c = new DataGridViewComboBoxCell();
            BuildAlgorithmsCombox(out c);
            Tapes_dataGridView.Rows[index].Cells["Type_Column"] = c;
            Tapes_dataGridView.Rows[index].Cells["Type_Column"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
            // Tapes_dataGridView.Rows[index].Cells["Type_Column"].Value = VideoAlgorithms.AllAlgorithms[1].Name;
            // NextPart_Column tells the part number of next part. 
            // NextX, NextY tell the approximate hole location for the next part. Incremented when a part is picked up.
            Tapes_dataGridView.Rows[index].Cells["NextPart_Column"].Value = "1";
            Tapes_dataGridView.Rows[index].Cells["Next_X_Column"].Value = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["Next_Y_Column"].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            // Z_Pickup_Column, Z_Place_Column: The Z values are measured when first part is placed. Picking up and
            // placing the next parts will then be faster.
            Tapes_dataGridView.Rows[index].Cells["Z_Pickup_Column"].Value = "--";
            Tapes_dataGridView.Rows[index].Cells["Z_Place_Column"].Value = "--";
            Tapes_dataGridView.Rows[index].Cells["TrayID_Column"].Value = "--";
            // Coordinates for parts: If set, optical system is not used, the coordinates are used directly
            // FirstXY sets the location of the first part. If LastXY is set (!=0, != first; there are more than one part on a tape),
            // The next part is <pitch> mm to the direction from first to last. If LastXY is not set (individual pickup location,
            // automatic feeder), orientation does not apply, rotation is used and manually set A correction is also used (it is
            // almost impossible to mount feeders or part holders exactly perpedicular).
            Tapes_dataGridView.Rows[index].Cells["CoordinatesForParts_Column"].Value = false;
            Tapes_dataGridView.Rows[index].Cells["UseNozzleCoordinates_Column"].Value = false;
            Tapes_dataGridView.Rows[index].Cells["LastX_Column"].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["LastY_Column"].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["RotationDirect_Column"].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            Invoke_TapeEditDialog(index, true);
        }

        private void TapeUp_button_Click(object sender, EventArgs e)
        {
            DataGrid_Up_button(Tapes_dataGridView);
        }

        private void TapeDown_button_Click(object sender, EventArgs e)
        {
            DataGrid_Down_button(Tapes_dataGridView);
        }

        private void DeleteTape_button_Click(object sender, EventArgs e)
        {
            // Need to do in two stages, because deleting a row can transfer selection state to another row (boo MS!)

            // make list of row indexes to delete, from high to low
            List<int> rowsToDelete = new List<int>();
            for (int i = Tapes_dataGridView.RowCount - 1; i >= 0; i--)
            {
                foreach (DataGridViewCell cell in Tapes_dataGridView.Rows[i].Cells)
                {
                    if (cell.Selected)
                    {
                        rowsToDelete.Add(i);
                        break;
                    }
                }
            }

            // delete rows 
            foreach (int i in rowsToDelete)
            {
                Tapes_dataGridView.Rows.RemoveAt(i);
            }
            Tapes_dataGridView.ClearSelection();

        }

        private void TapeGoTo_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (Tapes_dataGridView.SelectedCells.Count != 1)
            {
                return;
            };
            double X;
            double Y;
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["FirstX_Column"].Value.ToString().Replace(',', '.'), out X))
            {
                return;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["FirstY_Column"].Value.ToString().Replace(',', '.'), out Y))
            {
                return;
            }
            CNC_XYA_m(X, Y, Cnc.CurrentA);
        }

        private void TapeSet1_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (Tapes_dataGridView.SelectedCells.Count != 1)
            {
                return;
            };
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            Tapes_dataGridView.Rows[row].Cells["FirstX_Column"].Value = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[row].Cells["FirstY_Column"].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            // fix #22 update next coordinates when setting hole 1
            Tapes_dataGridView.Rows[row].Cells["NextPart_Column"].Value = "1";
            Tapes_dataGridView.Rows[row].Cells["Next_X_Column"].Value = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[row].Cells["Next_Y_Column"].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);

        }


        // ==========================================================================================================
        // Tapes_dataGridView_CellClick(): 
        private void Tapes_dataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if ((e.RowIndex < 0)||(e.ColumnIndex<0))
            {
                return;
            }
            // If the click is on a button column, resets the tape. 
            if (e.ColumnIndex == Tapes_dataGridView.Columns["SelectButton_Column"].Index)
            {
                Tapes.Reset(e.RowIndex);
                Update_GridView(Tapes_dataGridView);
                return;
            }
            // make drop down cells drop down on first click
            if (Tapes_dataGridView.CurrentCell is DataGridViewComboBoxCell)
            {
                Tapes_dataGridView.BeginEdit(true);
                ((ComboBox)Tapes_dataGridView.EditingControl).DroppedDown = true;
                Tapes_dataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }

        }


        // ==========================================================================================================
        // SelectTape(): 
        // Displays a dialog with buttons for all defined tapes, returns the ID of the tape.
        // used in placement when the user selects the tape to be used in runtime.
        // In this case, we remove the regular Tapes_dataGridView_CellClick() handler; the TapeDialog
        // changes the button text to "select" (and back to "reset" on return) and uses its own handler.
        private string SelectTape(string header)
        {
            System.Drawing.Point loc = Tapes_dataGridView.Location;
            Size size = Tapes_dataGridView.Size;
            DataGridView Grid = Tapes_dataGridView;
            TapeSelectionForm TapeDialog = new TapeSelectionForm(Grid, this);
            TapeDialog.HeaderString = header;
            Tapes_dataGridView.CellClick -= new DataGridViewCellEventHandler(Tapes_dataGridView_CellClick);
            this.Controls.Remove(Tapes_dataGridView);

            TapeDialog.ShowDialog(this);

            string ID = TapeDialog.ID;  // get the result
            DisplayText("Selected tape: " + ID);

            Tapes_tabPage.Controls.Add(Tapes_dataGridView);
            Tapes_dataGridView = Grid;
            Tapes_dataGridView.Location = loc;
            Tapes_dataGridView.Size = size;

            TapeDialog.Dispose();
            Tapes_dataGridView.CellClick += new DataGridViewCellEventHandler(Tapes_dataGridView_CellClick);
            return ID;
        }

        private void ResetOneTape_button_Click(object sender, EventArgs e)
        {
            for (int CurrentRow = 0; CurrentRow < Tapes_dataGridView.RowCount; CurrentRow++)
            {
                DataGridViewRow Row = Tapes_dataGridView.Rows[CurrentRow];
                bool DoRow = false;
                foreach (DataGridViewCell oneCell in Row.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }

                if (!DoRow)
                {
                    continue;
                };
                // Reset this component's tape:
                Tapes.Reset(Row.Index);
            }
            Update_GridView(Tapes_dataGridView);
        }

        private void ResetAllTapes_button_Click(object sender, EventArgs e)
        {
            Tapes.ClearAll();
        }

        private void SetPartNo_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            DataGridViewRow Row = Tapes_dataGridView.Rows[Tapes_dataGridView.CurrentCell.RowIndex];
            if (!int.TryParse(Row.Cells["NextPart_Column"].Value.ToString(), out _))
            {
                return;
            }
            Row.Cells["Next_X_Column"].Value = Cnc.CurrentX.ToString(CultureInfo.InvariantCulture);
            Row.Cells["Next_Y_Column"].Value = Cnc.CurrentY.ToString(CultureInfo.InvariantCulture);
        }

        private void Tape_GoToNext_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (Tapes_dataGridView.SelectedCells.Count != 1)
            {
                return;
            };
            double X;
            double Y;
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["Next_X_Column"].Value.ToString().Replace(',', '.'), out X))
            {
                return;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["Next_Y_Column"].Value.ToString().Replace(',', '.'), out Y))
            {
                return;
            }
            CNC_XYA_m(X, Y, Cnc.CurrentA);
        }

        private void Tape_resetZs_button_Click(object sender, EventArgs e)
        {
            for (int tape = 0; tape < Tapes_dataGridView.Rows.Count; tape++)
            {
                Tapes_dataGridView.Rows[tape].Cells["Z_Pickup_Column"].Value = "--";
                Tapes_dataGridView.Rows[tape].Cells["Z_Place_Column"].Value = "--";
            }
        }

        private void ResetPlaceZ_button_Click(object sender, EventArgs e)
        {
            for (int tape = 0; tape < Tapes_dataGridView.Rows.Count; tape++)
            {
                Tapes_dataGridView.Rows[tape].Cells["Z_Place_Column"].Value = "--";
            }
        }

        private void ResetSelectedZs(bool both)
        {
            bool DoRow = false;
            for (int tape = 0; tape < Tapes_dataGridView.Rows.Count; tape++)
            {
                foreach (DataGridViewCell oneCell in Tapes_dataGridView.Rows[tape].Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }
                if (DoRow)
                {
                    Tapes_dataGridView.Rows[tape].Cells["Z_Place_Column"].Value = "--";
                    if (both)
                    {
                        Tapes_dataGridView.Rows[tape].Cells["Z_Pickup_Column"].Value = "--";
                    }
                    DoRow = false;
                }
            }
        }

        private void ResetSelectedZs_button_Click(object sender, EventArgs e)
        {
            ResetSelectedZs(true);
        }

        private void ResetSelectedPlaceZs_button_Click(object sender, EventArgs e)
        {
            ResetSelectedZs(false);
        }

        private void SaveAllTapes_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SaveDataGrid(TapesAll_saveFileDialog.FileName, Tapes_dataGridView);
            }
        }

        private void LoadAllTapes_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                LoadTapesTable(TapesAll_openFileDialog.FileName);
            }
        }

        private void HoleTest_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence())
            {
                DisplayText("Machine is not homed, position is unknown.");
                return;
            }

            int PartNum = 0;
            int TapeNum = 0;
            if (UseCoordinatesDirectly(Tapes_dataGridView.CurrentCell.RowIndex))
            {
                DisplayText("\"Coordinates for parts\" is checked, hole location does not apply.");
                return;
            }

            DataGridViewRow Row = Tapes_dataGridView.Rows[Tapes_dataGridView.CurrentCell.RowIndex];
            if (!int.TryParse(HoleTest_maskedTextBox.Text, out PartNum))
            {
                if (!int.TryParse(Row.Cells["NextPart_Column"].Value.ToString(), out PartNum))
                {
                    DisplayText("Value in part # box is invalid.");
                    return;
                }
            }
            string Id = Row.Cells["Id_Column"].Value.ToString();
            double X = 0.0;
            double Y = 0.0;
            if (!Tapes.IdValidates_m(Id, out TapeNum))
            {
                return;
            }
            if (Tapes.GetPartHole_m(TapeNum, PartNum, out X, out Y))
            {
                CNC_XYA_m(X, Y, Cnc.CurrentA);
            }
        }

        private void ShowPartByCoordinates_m()
        {
            double X;
            double Y;
            double A;
            bool increment;
            if (!FindPartWithDirectCoordinates_m(Tapes_dataGridView.CurrentCell.RowIndex, out X, out Y, out A, out increment))
            {
                return;
            }
            CNC_XYA_m(X, Y, Cnc.CurrentA);
            DownCamera.ArrowAngle = A;
            DownCamera.DrawArrow = true;
        }

    private void ShowPart_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence())
            {
                DisplayText("Machine is not homed, position is unknown.");
                return;
            }

            int PartNum = 0;
            int TapeNum = 0;

            DataGridViewRow Row = Tapes_dataGridView.Rows[Tapes_dataGridView.CurrentCell.RowIndex];
            if (!int.TryParse(HoleTest_maskedTextBox.Text, out PartNum))
            {
                DisplayText("Value in part # box is invalid.");
                return;
            }
            // Tapes.GetPartLocationFromHolePosition_m() and ShowPartByCoordinates_m() use the next column from Tapes_dataGridView.
            // Set it temporarily, but remember what was there:
            string temp = Row.Cells["NextPart_Column"].Value.ToString();
            Row.Cells["NextPart_Column"].Value = PartNum.ToString(CultureInfo.InvariantCulture);

            if (UseCoordinatesDirectly(Tapes_dataGridView.CurrentCell.RowIndex))
            {
                ShowPartByCoordinates_m();
                Row.Cells["NextPart_Column"].Value = temp.ToString(CultureInfo.InvariantCulture);
                return;
            }

            string Id = Row.Cells["Id_Column"].Value.ToString();
            double X = 0.0;
            double Y = 0.0;
            if (!Tapes.IdValidates_m(Id, out TapeNum))
            {
                Row.Cells["NextPart_Column"].Value = temp.ToString(CultureInfo.InvariantCulture);
                return;
            }
            if (!Tapes.GetPartHole_m(TapeNum, PartNum, out X, out Y))
            {
                Row.Cells["NextPart_Column"].Value = temp.ToString(CultureInfo.InvariantCulture);
                return;
            }
            double pX = 0.0;
            double pY = 0.0;
            double A = 0.0;
            if (Tapes.GetPartLocationFromHolePosition_m(TapeNum, X, Y, out pX, out pY, out A))
            {
                CNC_XYA_m(pX, pY, Cnc.CurrentA);
            }
            DownCamera.ArrowAngle = A;
            DownCamera.DrawArrow = true;

            Row.Cells["NextPart_Column"].Value = temp.ToString(CultureInfo.InvariantCulture);
        }

        private void ShowPart_button_Leave(object sender, EventArgs e)
        {
            DownCamera.DrawArrow = false;
        }

        private void ShowPart_button_MouseLeave(object sender, EventArgs e)
        {
            DownCamera.DrawArrow = false;
        }

        // =================================================================================
        // even handlers for Tapes_dataGridView
        // see http://stackoverflow.com/questions/5652957/what-event-catches-a-change-of-value-in-a-combobox-in-a-datagridviewcell


        private void Tapes_dataGridView_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            // see http://stackoverflow.com/questions/4284370/datagridview-keydown-event-not-working-in-c-sharp
            if (e.Control is DataGridViewTextBoxEditingControl)
            {
                DataGridViewTextBoxEditingControl tb = e.Control as DataGridViewTextBoxEditingControl;
                tb.KeyDown += new KeyEventHandler(Tapes_dataGridView_MyKeyDown);
            }
        }

        private void Tapes_dataGridView_MyKeyDown(object sender, KeyEventArgs e)
        {
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            int col = Tapes_dataGridView.CurrentCell.ColumnIndex;
            // for pitch and offset columns, set width to custom
            if ((Tapes_dataGridView.Columns[col].Name == "Pitch_Column") ||
                (Tapes_dataGridView.Columns[col].Name == "OffsetX_Column") ||
                (Tapes_dataGridView.Columns[col].Name == "OffsetY_Column"))
            {
                Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value = "custom";
                return;
            }
        }

        private void Tapes_dataGridView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            int row = e.RowIndex;
            int col = e.ColumnIndex;
            if (StartingUp || LoadingDataGrid)
            {
                return;
            }
            if ((col < 0) || (col > Tapes_dataGridView.Columns.Count))
            {
                return;
            }
            if (row < 0)
            {
                return;
            }

            // if width dropdown, fill values automatically
            if (((Tapes_dataGridView.Columns[col].HeaderText == "Width") && (row >= 0)))
            {
                double X;
                double Y;
                double pitch;
                if (Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value != null)
                {
                    if (Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value.ToString() != "custom")
                    {
                        TapeWidthStringToValues(Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value.ToString(), out X, out Y, out pitch);
                        Tapes_dataGridView.Rows[row].Cells["Pitch_Column"].Value = pitch.ToString(CultureInfo.InvariantCulture);
                        Tapes_dataGridView.Rows[row].Cells["OffsetX_Column"].Value = X.ToString(CultureInfo.InvariantCulture);
                        Tapes_dataGridView.Rows[row].Cells["OffsetY_Column"].Value = Y.ToString(CultureInfo.InvariantCulture);
                    }
                    return;
                }
            }

            // commented out until I find how to make it work at the keystroke (MS insists on end edit, too late)
            //double val;
            //if (((Tapes_dataGridView.CurrentCell.OwningColumn.HeaderText == "Pitch") &&
            //     (Tapes_dataGridView.CurrentCell.RowIndex >= 0)))
            //{
            //    if (Tapes_dataGridView.CurrentCell.Value != null)
            //    {
            //        if (double.TryParse(Tapes_dataGridView.CurrentCell.Value.ToString(), out val))
            //        {
            //            Tapes_dataGridView.CurrentCell.Style.ForeColor = Color.Black;
            //        }
            //        else
            //        {
            //            Tapes_dataGridView.CurrentCell.Style.ForeColor = Color.Red;
            //        }
            //    }
            //}

            // fix #22 calculate new next coordinates if column was changed
            // only update next coordinates if corresponding column has been changed and rowIndex > 0
        }

        private void Tapes_dataGridView_CellLeave(object sender, DataGridViewCellEventArgs e)
        {
            int row = e.RowIndex;
            int col = e.ColumnIndex;
            if (StartingUp || LoadingDataGrid)
            {
                return;
            }
            if ((col < 0) || (col > Tapes_dataGridView.Columns.Count))
            {
                return;
            }
            if (row < 0)
            {
                return;
            }

            if ((Tapes_dataGridView.Columns[e.ColumnIndex].HeaderText == "Next") && (e.RowIndex >= 0))
            {
                int NextNo = 1;

                if (Tapes_dataGridView.Rows[row].Cells["NextPart_Column"].Value == null)
                {
                    ShowMessageBox(
                        "Bad data in Next",
                        "Data error",
                        MessageBoxButtons.OK);
                    return;
                }

                if (!int.TryParse(Tapes_dataGridView.Rows[row].Cells["NextPart_Column"].Value.ToString(), out NextNo))
                {
                    ShowMessageBox(
                        "Bad data in Next",
                        "Data error",
                        MessageBoxButtons.OK);
                    return;
                }

                if (Tapes != null)
                {
                    Tapes.UpdateNextCoordinates(row, NextNo);
                }
                return;
            }
        }

        // =================================================================================
        // Trays:

        private void SaveTray_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            // Get current tray ID
            int CurrRow = Tapes_dataGridView.CurrentCell.RowIndex;
            string TrayID = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].Value.ToString();

            // Copy the tapes with this ID to Clipboard datagridview:
            DataGridView ClipBoard_dgw = new DataGridView();
            ClipBoard_dgw.AllowUserToAddRows = false;  // this prevents an empty row in the end
            foreach (DataGridViewColumn col in Tapes_dataGridView.Columns)
            {
                ClipBoard_dgw.Columns.Add(col.Clone() as DataGridViewColumn);
            }
            DataGridViewRow NewRow = new DataGridViewRow();
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                if (row.Cells["TrayID_Column"].Value.ToString() == TrayID)
                {
                    NewRow = (DataGridViewRow)row.Clone();
                    int intColIndex = 0;
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        NewRow.Cells[intColIndex].Value = cell.Value;
                        intColIndex++;
                    }
                    ClipBoard_dgw.Rows.Add(NewRow);
                }
            }

            // Save the Clipboard
            SaveDataGrid(TapesAll_saveFileDialog.FileName, ClipBoard_dgw);
        }

        private void LoadTrayFromFile(string FileName)
        {
            // from: http://stackoverflow.com/questions/6336239/copy-datagridviews-rows-into-another-datagridview  
            DataGridView ClipBoard_dgw = new DataGridView();
            ClipBoard_dgw.AllowUserToAddRows = false;  // this prevents an empty row in the end
            foreach (DataGridViewColumn col in Tapes_dataGridView.Columns)
            {
                ClipBoard_dgw.Columns.Add(col.Clone() as DataGridViewColumn);
            }
            LoadTapesFromFile(FileName, ClipBoard_dgw);
            DataGridViewRow NewRow = new DataGridViewRow();
            foreach (DataGridViewRow row in ClipBoard_dgw.Rows)
            {
                NewRow = (DataGridViewRow)row.Clone();
                int intColIndex = 0;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    NewRow.Cells[intColIndex].Value = cell.Value;
                    intColIndex++;
                }
                Tapes_dataGridView.Rows.Add(NewRow);
            }
            Update_GridView(Tapes_dataGridView);
        }

        private void LoadTray_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            LoadTrayFromFile(TapesAll_openFileDialog.FileName);
        }

        private void DeleteTray(string TrayID, int col_index)
        {
            // removes a tray from Tapes_dataGridView
            // Can't modify the gridview and iterate through it at the same time, so:

            // Copy current to clipboard
            DataGridView ClipBoard_dgw = new DataGridView();
            ClipBoard_dgw.AllowUserToAddRows = false;  // this prevents an empty row in the end
            // create columns
            foreach (DataGridViewColumn col in Tapes_dataGridView.Columns)
            {
                ClipBoard_dgw.Columns.Add(new DataGridViewColumn(col.CellTemplate));
            }
            // copy rows
            DataGridViewRow NewRow = new DataGridViewRow();
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                NewRow = (DataGridViewRow)row.Clone();
                int intColIndex = 0;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    NewRow.Cells[intColIndex].Value = cell.Value;
                    intColIndex++;
                }
                ClipBoard_dgw.Rows.Add(NewRow);
            }

            Tapes_dataGridView.Rows.Clear();   // Clear existing
            // Copy back if needed
            foreach (DataGridViewRow row in ClipBoard_dgw.Rows)
            {
                NewRow = (DataGridViewRow)row.Clone();
                int intColIndex = 0;
                if (row.Cells[col_index].Value.ToString() != TrayID)
                {
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        NewRow.Cells[intColIndex].Value = cell.Value;
                        intColIndex++;
                    }
                    Tapes_dataGridView.Rows.Add(NewRow);
                }
            }
        }

        private void ReplaceTray_button_Click(object sender, EventArgs e)
        {
            TapesAll_openFileDialog.Filter = "LitePlacer tape files| *.tapes_v2; *.tapes | All files | *.* ";;

            if (TapesAll_openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            // Get current tray ID
            int CurrRow = Tapes_dataGridView.CurrentCell.RowIndex;
            string TrayID = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].Value.ToString();
            int col = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].ColumnIndex;
            DeleteTray(TrayID, col);
            LoadTrayFromFile(TapesAll_openFileDialog.FileName);
        }

        private void ReloadTray_button_Click(object sender, EventArgs e)
        {
            // Get current tray ID
            int CurrRow = Tapes_dataGridView.CurrentCell.RowIndex;
            string TrayID = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].Value.ToString();
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                if (row.Cells["TrayID_Column"].Value.ToString() == TrayID)
                {
                    row.Cells["NextPart_Column"].Value = 1;
                }
            }
        }

 
        #endregion  TapeNumber Positions page functions

        // =================================================================================
        // Test functions
        // =================================================================================
        #region test functions

        // =================================================================================
        private void LabelTestButtons()
        {
            Test1_button.Text = "Pickup this";
            Test2_button.Text = "Place here";
            Test3_button.Text = "Probe down";
            Test4_button.Text = "Probe (n.c.)";
            Test5_button.Text = "Nozzle up";
            Test6_button.Text = "Nozzle to down cam";
            Test7_button.Text = "Nozzle to up cam";
        }

        // test 1

        private void Test1_button_Click(object sender, EventArgs e)
        {
            DisplayText("test 1: Pick up this (probing)");
            if (!Check_HeightCalibrationDone_m()) return;
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            if (!CNC_Z_m(0))
            {
                return;
            }
            CNC_Z_m(0);
            PumpOn();
            VacuumOff();
            if (!Nozzle.Move_m(Cnc.CurrentX, Cnc.CurrentY, Cnc.CurrentA))
            {
                PumpOff();
                return;
            }
            if (!Nozzle_ProbeDown_m(Setting.Placement_Pickup_Depth))
            {
                return;
            }
            VacuumOn();
            CNC_Z_m(0);  // pick up
        }

        // =================================================================================
        // test 2

        // static int test2_state = 0;
        private void Test2_button_Click(object sender, EventArgs e)
        {
            DisplayText("test 2: Place here (probing)");
            if (!Check_HeightCalibrationDone_m()) return;
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            if (!Cnc.Z(0))
            {
                return;
            }
            double Xmark = Cnc.CurrentX;
            double Ymark = Cnc.CurrentY;
            if (!Nozzle.Move_m(Cnc.CurrentX, Cnc.CurrentY, Cnc.CurrentA))
            {
                return;
            }
            if (!Nozzle_ProbeDown_m(Setting.Placement_Placement_Depth))
            {
                return;
            }
            VacuumOff();
            PumpOff();
            if (!Cnc.Z(0))
            {
                return;
            }
            CNC_XYA_m(Xmark, Ymark, Cnc.CurrentA);  // show results
        }

        // =================================================================================
        // test 3 

        private void Test3_button_Click(object sender, EventArgs e)
        {
            // Probe (no correction)
            DisplayText("test 3: Probe down (using nozzle correction)");
            if (!Check_HeightCalibrationDone_m()) return;
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            if (!Cnc.Z(0))
            {
                DisplayText("Nozzle already down");
                return;
            }
            if (!Nozzle.Move_m(Cnc.CurrentX, Cnc.CurrentY, Cnc.CurrentA))
            {
                return;
            }
            Nozzle_ProbeDown_m(0);
        }


        // =================================================================================
        // test 4 

        private void Test4_button_Click(object sender, EventArgs e)
        {
            DisplayText("test 4: Probe down (no correction)");
            if (!Check_HeightCalibrationDone_m()) return;
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }

            if (!Cnc.Z(0))
            {
                DisplayText("Nozzle already down");
                return;
            }
            CNC_XYA_m((Cnc.CurrentX + Setting.DownCam_NozzleOffsetX),
                        (Cnc.CurrentY + Setting.DownCam_NozzleOffsetY), Cnc.CurrentA);
            Nozzle_ProbeDown_m(0);
        }

        // =================================================================================
        // test 5

        private void Test5_button_Click(object sender, EventArgs e)
        {
            DisplayText("test 5: Nozzle up");
            if (MovementIsBusy_m())
            {
                return;
            }
            CNC_Z_m(0);  // go up
        }

        // =================================================================================
        // test 6
        private void Test6_button_Click(object sender, EventArgs e)
        {
            DisplayText("test 6: Nozzle down cam");
            if (MovementIsBusy_m())
            {
                return;
            }
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }
            if (!Cnc.Z(0))
            {
                DisplayText("Z not up");
                return;
            }
            CNC_XYA_m((Cnc.CurrentX + Setting.DownCam_NozzleOffsetX),
                        (Cnc.CurrentY + Setting.DownCam_NozzleOffsetY), Cnc.CurrentA);
        }

        // =================================================================================
        // test 7
        private void Test7_button_Click(object sender, EventArgs e)
        {
            DisplayText("test 7: Nozzle to up cam (do nozzle down to check)");
            if (!CheckPositionConfidence()) return;
            if (MovementIsBusy_m())
            {
                return;
            }
            if (!Cnc.Z(0))
            {
                DisplayText("Z not up");
                return;
            }
            double xp = Setting.UpCam_PositionX;
            double xo = Setting.DownCam_NozzleOffsetX;
            double yp = Setting.UpCam_PositionY;
            double yo = Setting.DownCam_NozzleOffsetY;
            Nozzle.Move_m(xp - xo, yp - yo, Cnc.CurrentA);
        }

        #endregion test functions

        // ==========================================================================================================
        // Measurement boxes (Homing, Nozzle, OriginalFiducials, Tapes etc)
        // ==========================================================================================================
        #region Measurementboxes

        // ==========================================================================================================
        // Some helpers:

        public void DataGridViewCopy(DataGridView FromGr, ref DataGridView ToGr, bool print = true)
        {
            DataGridViewRow NewRow;
            ToGr.Rows.Clear();

            foreach (DataGridViewRow Row in FromGr.Rows)
            {
                NewRow = (DataGridViewRow)Row.Clone();
                for (int i = 0; i < Row.Cells.Count; i++)
                {
                    NewRow.Cells[i].Value = Row.Cells[i].Value;
                }
                ToGr.Rows.Add(NewRow);
            }
            if (print)
            {
                // Print results:
                string DebugStr = FromGr.Name + " => " + ToGr.Name + ":";
                DebugStr = DebugStr.Replace("_dataGridView", "");
                if (ToGr.Rows.Count == 0)
                {
                    DisplayText(DebugStr + "( )");
                }
                else
                {
                    foreach (DataGridViewRow Row in ToGr.Rows)
                    {
                        DebugStr += "( ";
                        for (int i = 0; i < Row.Cells.Count; i++)
                        {
                            if (Row.Cells[i].Value == null)
                            {
                                DebugStr += "--, ";
                            }
                            else
                            {
                                DebugStr += Row.Cells[i].Value.ToString() + ", ";
                            }
                        }
                        DebugStr += ")\n";
                        DebugStr = DebugStr.Replace(", )\n", " )");
                        DisplayText(DebugStr);
                    }
                }
            }
        }

        // ==========================================================================================================
        // DownCam:

        // ==========================================================================================================
        // Snapshot:
        /*
        private void UpCam_SnapshotToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref UpcamSnapshot_dataGridView);
        }

        private void UpCam_SnapshotToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(UpcamSnapshot_dataGridView, ref Display_dataGridView);
            UpCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        private void UpCam_TakeSnapshot_button_Click(object sender, EventArgs e)
        {
            UpCam_TakeSnapshot();
        }

        private void UpCam_TakeSnapshot()
        {
            SelectCamera(UpCamera);
            DisplayText("UpCam_TakeSnapshot()");
            UpCamera.SnapshotRotation = Cnc.CurrentA;
            UpCamera.BuildMeasurementFunctionsList(UpcamSnapshot_dataGridView);
            UpCamera.TakeSnapshot();

            DownCamera.SnapshotOriginalImage = new Bitmap(UpCamera.SnapshotImage);
            DownCamera.SnapshotImage = new Bitmap(UpCamera.SnapshotImage);

            // We need a copy of the snapshot to scale it, in 24bpp format. See http://stackoverflow.com/questions/2016406/converting-bitmap-pixelformats-in-c-sharp
            Bitmap Snapshot24bpp = new Bitmap(640, 480, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (Graphics gr = Graphics.FromImage(Snapshot24bpp))
            {
                gr.DrawImage(UpCamera.SnapshotOriginalImage, new Rectangle(0, 0, 640, 480));
            }
            // scale:
            double Xscale = Setting.UpCam_XmmPerPixel / Setting.DownCam_XmmPerPixel;
            double Yscale = Setting.UpCam_YmmPerPixel / Setting.DownCam_YmmPerPixel;
            double zoom = UpCamera.GetMeasurementZoom();
            Xscale = Xscale / zoom;
            Yscale = Yscale / zoom;
            int SnapshotSizeX = (int)(Xscale * 640);
            int SnapshotSizeY = (int)(Yscale * 480);
            // SnapshotSize is the size (in pxls) of upcam snapshot, scaled so that it draws in correct size on downcam image.
            ResizeNearestNeighbor RezFilter = new ResizeNearestNeighbor(SnapshotSizeX, SnapshotSizeY);
            Bitmap ScaledShot = RezFilter.Apply(Snapshot24bpp);  // and this is the scaled image
            // Mirror:
            Mirror MirrFilter = new Mirror(false, true);
            MirrFilter.ApplyInPlace(ScaledShot);

            // Clear DownCam image
            Graphics DownCamGr = Graphics.FromImage(DownCamera.SnapshotImage);
            DownCamGr.Clear(Color.Black);
            // Embed the ScaledShot to it. Upper left corner of the embedding is:
            int X = 320 - SnapshotSizeX / 2;
            int Y = 240 - SnapshotSizeY / 2;
            DownCamGr.DrawImage(ScaledShot, X, Y, SnapshotSizeX, SnapshotSizeY);
            DownCamera.SnapshotImage.MakeTransparent(Color.Black);
            // DownCam Snapshot is ok, copy it to original too
            DownCamera.SnapshotOriginalImage = new Bitmap(DownCamera.SnapshotImage);

            DownCamera.SnapshotRotation = Cnc.CurrentA;
        }

        private void UpcamSnapshot_ColorBox_MouseClick(object sender, MouseEventArgs e)
        {
            // Show the color dialog.
            DialogResult result = colorDialog1.ShowDialog();
            // See if user pressed ok.
            if (result == DialogResult.OK)
            {
                // Set form background to the selected color.
                UpcamSnapshot_ColorBox.BackColor = colorDialog1.Color;
                Setting.UpCam_SnapshotColor = colorDialog1.Color;
                UpCamera.SnapshotColor = colorDialog1.Color;
            }
        }


        private void DownCam_SnapshotToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref DowncamSnapshot_dataGridView);
        }

        private void DownCam_SnapshotToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(DowncamSnapshot_dataGridView, ref Display_dataGridView);
            DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        private void DownCam_TakeSnapshot_button_Click(object sender, EventArgs e)
        {
            DownCamera.SnapshotRotation = Cnc.CurrentA;
            DownCamera.BuildMeasurementFunctionsList(DowncamSnapshot_dataGridView);
            DownCamera.TakeSnapshot();
        }

        private void DowncamSnapshot_ColorBox_MouseClick(object sender, MouseEventArgs e)
        {
            // Show the color dialog.
            DialogResult result = colorDialog1.ShowDialog();
            // See if user pressed ok.
            if (result == DialogResult.OK)
            {
                // Set form background to the selected color.
                DowncamSnapshot_ColorBox.BackColor = colorDialog1.Color;
                Setting.DownCam_SnapshotColor = colorDialog1.Color;
                DownCamera.SnapshotColor = colorDialog1.Color;
            }
        }

        */
         #endregion  Measurementboxes


        // ==========================================================================================================
        // Nozzles
        // ==========================================================================================================
        #region Nozzles

        // Note: Datagrid rows go from 0 to n, we show nozzles from 1 to n.
        // When referring to nozzle, think nozzle no, which is datagridrow + 1 (and vice versa).

        // datagrid column numbers
        const int Nozzledata_NozzleNoColumn = 0;
        const int Nozzledata_StartXColumn = 1;
        const int Nozzledata_StartYColumn = 2;
        const int Nozzledata_StartZColumn = 3;
        // Rest are move 1 axis, move 1 amount, move 2 axis, ...
        // == Nozzledata_StartZColumn+(moveno-1)*2+1 for axis, Nozzledata_StartZColumn+(moveno-1)*2+2 for amount.

        const int NoOfNozzleMoves = 8;

        // ==========================================================================================================
        // Program housekeeping
        // ==========================================================================================================
 
        private void Nozzles_initialize()
        {
            ContextmenuLoadNozzle = Setting.Nozzles_default;
            ContextmenuUnloadNozzle = Setting.Nozzles_default;
            DisplayText("Loading nozzles data");
            // build tables
            BuildNozzleTable(NozzlesLoad_dataGridView);
            BuildNozzleTable(NozzlesUnload_dataGridView);
            NoOfNozzles_UpDown.Value = Setting.Nozzles_count;
            // fill values
            string path = GetPath();
            LoadDataGrid(path + NOZZLES_LOAD_DATAFILE, NozzlesLoad_dataGridView, DataTableType.Nozzles);
            LoadDataGrid(path + NOZZLES_UNLOAD_DATAFILE, NozzlesUnload_dataGridView, DataTableType.Nozzles);
            LoadDataGrid(path + NOZZLES_VISIONPARAMETERS_DATAFILE, NozzlesParameters_dataGridView, DataTableType.Nozzles);
            Nozzle.LoadNozzlesCalibration(path + NOZZLES_CALIBRATION_DATAFILE);
            AdjustNozzleDataSizes();
            FillNozzlesParameters_dataGridView();
            Nozzle.UpdateNozzleGridView();
        }


        private void AdjustNozzleDataSizes()
        {
            int UnloadCount = NozzlesUnload_dataGridView.Rows.Count;
            int LoadCount = NozzlesLoad_dataGridView.Rows.Count;
            int ParameterCount = NozzlesParameters_dataGridView.Rows.Count;
            int CalibrationCount = Nozzle.NozzleDataAllNozzles.Count;
            bool CountOk = (UnloadCount == Setting.Nozzles_count) && (LoadCount == Setting.Nozzles_count) &&
                (ParameterCount == Setting.Nozzles_count) && (CalibrationCount == Setting.Nozzles_count);
            if (CountOk)
            {
                return;
            }
            bool SaveStarting = StartingUp;
            StartingUp = false;
            DialogResult dialogResult = ShowMessageBox(
                "Nozzle data sizes on disk don't match nozzle count in program settings.\n\r" +
                "Nozzle count = " + Setting.Nozzles_count.ToString() + "\n\r" +
                "Count of load moves = " + LoadCount.ToString() + "\n\r" +
                "Count of unload moves = " + UnloadCount.ToString() + "\n\r" +
                "Count of vision parameters = " + ParameterCount.ToString() + "\n\r" +
                "Count of calibration data = " + CalibrationCount.ToString() + "\n\r" +
                "If you didn't excpect this:" + "\n\r" +
                "Before clicking Yes or No, take a backup copy of your LitePlacer directory." + "\n\r" +
                "Click Yes: The data size is adjusted to stored nozzle count," + "\n\r" +
                "resulting to possible loss of data." + "\n\r" + "Click Yes: The data size is adjusted to stored nozzle count," + "\n\r" +
                "Click No: The data is loaded as is. Results are unpredictable," + "\n\r" +
                "program may crash and nozzle data needs reviewing." + "\n\r" +
                "Clicking Cancel will exit without changes.",
                "Adjust nozzle data sizes?", MessageBoxButtons.YesNoCancel);
            if (dialogResult == DialogResult.Cancel)
            {
                Environment.Exit(0);
            };
            if (dialogResult == DialogResult.Yes)
            {
                StartingUp = SaveStarting;
                AdjustNozzleGrid(NozzlesUnload_dataGridView);
                AdjustNozzleGrid(NozzlesLoad_dataGridView);
                AdjustNozzleGrid(NozzlesParameters_dataGridView);
                Nozzle.AdjustNozzleCalibrationDataCount();
            };
        }

        private void AdjustNozzleGrid(DataGridView Grid)
        {
            // add
            while (Grid.Rows.Count < Setting.Nozzles_count)
            {
                Grid.Rows.Insert(Grid.Rows.Count);
            }
            // or remove
            if (Grid.Rows.Count > Setting.Nozzles_count)
            {
                Grid.RowCount = Setting.Nozzles_count;
            }
        }

        // ==========================================================================================================
        // tab page enter/leave

        private double NozzletabStore_XYspeed = 500.0;
        private double NozzletabStore_Zspeed = 500.0;
        private double NozzletabStore_Aspeed = 500.0;
        private int NozzletabStore_timeout = 10;
        private bool NozzletabStore_slack = false;
        private bool NozzletabStore_slackA = false;

        private bool AtNozzlesTab = false;

        private void Nozzles_tabPage_Begin()
        {
            DisplayText("Setup Nozzles tab begin");
            NozzeTip_textBox.Visible = true;
            FillNozzlesParameters_dataGridView();   // algorithm list may have changed
            if (NozzleZGuard_checkBox.Checked)
            {
                ZGuardOff();
            }
            else
            {
                ZGuardOn();
            }
            // disable z switches, otherwise you can't do setup 
            ZGuardOff();
            Cnc.DisableZswitches();

            NozzleChangeEnable_checkBox.Checked = Setting.Nozzles_Enabled;
            NozzleXYspeed_textBox.Text = Setting.Nozzles_XYspeed.ToString(CultureInfo.InvariantCulture);
            NozzleZspeed_textBox.Text = Setting.Nozzles_Zspeed.ToString(CultureInfo.InvariantCulture);
            NozzleAspeed_textBox.Text = Setting.Nozzles_Aspeed.ToString(CultureInfo.InvariantCulture);
            NozzleTimeout_textBox.Text = Setting.Nozzles_Timeout.ToString(CultureInfo.InvariantCulture);
            NozzleXYFullSpeed_checkBox.Checked = Setting.Nozzles_XYfullSpeed;
            NozzleZFullSpeed_checkBox.Checked = Setting.Nozzles_ZfullSpeed;
            NozzleAFullSpeed_checkBox.Checked = Setting.Nozzles_AfullSpeed;
            FirstMoveFullSpeed_checkBox.Checked = Setting.Nozzles_FirstMoveFullSpeed;
            Nozzle1stMoveSlackComp_checkBox.Checked = Setting.Nozzles_FirstMoveSlackCompensation;
            LastMoveFullSpeed_checkBox.Checked = Setting.Nozzles_LastMoveFullSpeed;
            ForceNozzle_numericUpDown.Maximum = Setting.Nozzles_count;
            NozzleWarning_textBox.Text = Setting.Nozzles_WarningTreshold.ToString("0.00", CultureInfo.InvariantCulture);


            // For setup and testing, we want all operations (including jog and go button) to obey speed settings.
            // We don't want to disturb other operations, so we'll store the current state at page enter and restore at page leave.
            // store cnc speed settings
            NozzletabStore_XYspeed = Cnc.NozzleSpeedXY;
            NozzletabStore_Zspeed = Cnc.NozzleSpeedZ;
            NozzletabStore_Aspeed = Cnc.NozzleSpeedA;
            NozzletabStore_timeout = CNC_timeout;
            NozzletabStore_slack = Cnc.SlackCompensation;
            NozzletabStore_slackA = Cnc.SlackCompensationA;

            // replace with nozzle speed settings
            Cnc.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Cnc.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Cnc.SlowA = !Setting.Nozzles_AfullSpeed;
            Cnc.NozzleSpeedXY = Setting.Nozzles_XYspeed;
            Cnc.NozzleSpeedZ = Setting.Nozzles_Zspeed;
            Cnc.NozzleSpeedA = Setting.Nozzles_Aspeed;
            CNC_timeout = Setting.Nozzles_Timeout;
            Cnc.SlackCompensation = false;  // nozzle changes without slack compensation
            Cnc.SlackCompensationA = false;

            ResizeNozzleTables();
            AtNozzlesTab = true;
        }

        private void Nozzles_tabPage_End()
        {
            ZGuardOn();
            // enable switches
            Cnc.EnableZswitches();
            // restore settings
            Cnc.SlowXY = false;
            Cnc.SlowZ = false;
            Cnc.SlowA = false;
            Cnc.NozzleSpeedXY = NozzletabStore_XYspeed;
            Cnc.NozzleSpeedZ = NozzletabStore_Zspeed;
            Cnc.NozzleSpeedA = NozzletabStore_Aspeed;
            CNC_timeout = NozzletabStore_timeout;
            Cnc.SlackCompensation = NozzletabStore_slack;
            Cnc.SlackCompensationA = NozzletabStore_slackA;


            AtNozzlesTab = false;
        }


        // ==========================================================================================================
        // Save data

        private void NozzlesSave_button_Click(object sender, EventArgs e)
        {
            string path = GetPath();
            SaveDataGrid(path + NOZZLES_LOAD_DATAFILE, NozzlesLoad_dataGridView);
            SaveDataGrid(path + NOZZLES_UNLOAD_DATAFILE, NozzlesUnload_dataGridView);
            SaveDataGrid(path + NOZZLES_VISIONPARAMETERS_DATAFILE, NozzlesParameters_dataGridView);
            Nozzle.SaveNozzlesCalibration(path + NOZZLES_CALIBRATION_DATAFILE);
        }

        private bool Nozzles_Stop = false;
        private void NozzlesStop_button_Click(object sender, EventArgs e)
        {
            Nozzles_Stop = true;
        }


        // ==========================================================================================================
        // Datagirds
        void AddNozzleAlgorithmNames(int col)
        {
            // NozzlesParameters_dataGridView.Rows.Add(new DataGridViewRow());
            NozzlesParameters_dataGridView.Rows[col].Cells["NozzleNumber_column"].Value = (col+1).ToString();

            string AlgName = "";
            if (NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"].Value != null)
            {
                AlgName = NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"].Value.ToString();
            }

                // add algorithm names
                ((DataGridViewComboBoxCell)NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"]).Items.Clear();
            foreach (VideoAlgorithmsCollection.FullAlgorithmDescription alg in VideoAlgorithms.AllAlgorithms)
            {
                ((DataGridViewComboBoxCell)NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"]).Items.Add(alg.Name);
            }
                ((DataGridViewComboBoxCell)NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"]).Items.Add("-- not set --");

            // select current, if exists
            if (((DataGridViewComboBoxCell)NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"]).
                    Items.Contains(AlgName))
            {
                NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"].Value = AlgName;
            }
            else
            {
                if ((AlgName != "-- not set --")&& (AlgName != ""))
                {
                    DisplayText("Warning: Stored nozzle video algorithm name \"" + AlgName
                        + "\" does not exist", KnownColor.DarkRed, true);
                }
                NozzlesParameters_dataGridView.Rows[col].Cells["VisionAlgorithm_column"].Value = "-- not set --";
            }
        }

        void FillNozzlesParameters_dataGridView()
        {
            for (int i = 0; i < Setting.Nozzles_count; i++)
            {
                // number the rows
                if (i<NozzlesLoad_dataGridView.RowCount && i < NozzlesUnload_dataGridView.RowCount)
                {
                    NozzlesLoad_dataGridView.Rows[i].Cells[0].Value = (i + 1).ToString();
                    NozzlesUnload_dataGridView.Rows[i].Cells[0].Value = (i + 1).ToString();
                    AddNozzleAlgorithmNames(i);
                }
            }
        }


        private void ResizeNozzleTables()
        {
            int height = 2 * SystemInformation.BorderSize.Height;
            foreach (DataGridViewRow row in NozzlesLoad_dataGridView.Rows)
            {
                height += row.Height;
            }

            System.Drawing.Size size = NozzlesLoad_dataGridView.Size;
            size.Height = height + NozzlesLoad_dataGridView.ColumnHeadersHeight;
            NozzlesLoad_dataGridView.Size = size;

            size = NozzlesUnload_dataGridView.Size;
            size.Height = height + NozzlesLoad_dataGridView.ColumnHeadersHeight + SystemInformation.VerticalScrollBarWidth;
            NozzlesUnload_dataGridView.Size = size;

            size = NozzlesParameters_dataGridView.Size;
            size.Height = height + NozzlesParameters_dataGridView.ColumnHeadersHeight;
            NozzlesParameters_dataGridView.Size = size;
        }

        private void BuildNozzleTable(DataGridView Grid)
        {
            for (int i = 1; i <= NoOfNozzleMoves; i++)
            {
                DataGridViewComboBoxColumn ComboCol = new DataGridViewComboBoxColumn();
                ComboCol.Items.Add("X");
                ComboCol.Items.Add("Y");
                ComboCol.Items.Add("Z");
                ComboCol.Items.Add("--");
                ComboCol.HeaderText = "move" + i.ToString(CultureInfo.InvariantCulture) + " axis";
                ComboCol.Width = 44;
                ComboCol.Name = "MoveNumber" + i.ToString(CultureInfo.InvariantCulture) + "axis_Column";
                Grid.Columns.Add(ComboCol);
                DataGridViewTextBoxColumn TextCol = new DataGridViewTextBoxColumn();
                TextCol.HeaderText = "move" + i.ToString(CultureInfo.InvariantCulture) + " amount";
                TextCol.Width = 48;
                TextCol.Name = "MoveNumber" + i.ToString(CultureInfo.InvariantCulture) + "amount_Column";
                Grid.Columns.Add(TextCol);
            }
            foreach (DataGridViewColumn column in Grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
        }


        // ==========================================================================================================
        // Context menus:

        // The load and unload datagrids have context menu. We want to know which nozzle the user right-clicked:
        private int ContextmenuLoadNozzle = 1;
        private int ContextmenuUnloadNozzle = 1;

        // Doh! mouseclick right button event does not fire if context menu is set! So, we need to keep track of the cell
        // the mouse is over and if right button goes down, make a note of the position.
        private int LoadMouseEnterRow;
        private int UnloadMouseEnterRow;

        private void NozzlesLoad_dataGridView_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            LoadMouseEnterRow = e.RowIndex;
        }

        private void NozzlesLoad_dataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ContextmenuLoadNozzle = LoadMouseEnterRow + 1;
            }
        }

        private void NozzlesUnload_dataGridView_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            UnloadMouseEnterRow = e.RowIndex;
        }

        private void NozzlesUnload_dataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ContextmenuUnloadNozzle = UnloadMouseEnterRow + 1;
            }
        }


        // ==========================================================================================================
        // Context (right click) menu items

        private void gotoStartPositionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (ContextmenuLoadNozzle == 0)
            {
                DisplayText("Goto load start - header click, ignored", KnownColor.DarkGreen);
                return;
            }
            DisplayText("Goto load start", KnownColor.DarkGreen);
            m_NozzleGotoStart(NozzlesLoad_dataGridView, ContextmenuLoadNozzle);
        }

        private void gotoUnloadStartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            if (ContextmenuUnloadNozzle == 0)
            {
                DisplayText("Goto unload start - header click, ignored", KnownColor.DarkGreen);
                return;
            }
            DisplayText("Goto unload start", KnownColor.DarkGreen);
            m_NozzleGotoStart(NozzlesUnload_dataGridView, ContextmenuUnloadNozzle);
        }

        // ==========================================================================================================
        private void AddNozzle()
        {
            int RowNo = NozzlesLoad_dataGridView.Rows.Count;
            NozzlesLoad_dataGridView.Rows.Insert(RowNo);
            NozzlesUnload_dataGridView.Rows.Insert(RowNo);
            NozzlesParameters_dataGridView.Rows.Insert(RowNo);
            AddNozzleAlgorithmNames(RowNo);
            RowNo++;
            NozzlesLoad_dataGridView.Rows[RowNo - 1].Cells[Nozzledata_NozzleNoColumn].Value = RowNo.ToString(CultureInfo.InvariantCulture);
            NozzlesUnload_dataGridView.Rows[RowNo - 1].Cells[Nozzledata_NozzleNoColumn].Value = RowNo.ToString(CultureInfo.InvariantCulture);
            NozzlesParameters_dataGridView.Rows[RowNo - 1].Cells[Nozzledata_NozzleNoColumn].Value = RowNo.ToString(CultureInfo.InvariantCulture);
            ResizeNozzleTables();
            Nozzle.AdjustNozzleCalibrationDataCount();
        }

        private void RemoveNozzle()
        {
            NozzlesLoad_dataGridView.Rows.RemoveAt(NozzlesLoad_dataGridView.Rows.Count - 1);
            NozzlesUnload_dataGridView.Rows.RemoveAt(NozzlesUnload_dataGridView.Rows.Count - 1);
            NozzlesParameters_dataGridView.Rows.RemoveAt(NozzlesParameters_dataGridView.Rows.Count - 1);
            ResizeNozzleTables();
            Nozzle.AdjustNozzleCalibrationDataCount();
        }



        // ==========================================================================================================
        // Vision and calibration stuff
        // ==========================================================================================================

        private bool CheckSizeData_m(out double Smin, out double Smax)
        {
            // Checks the validity of the data in min and max size columns for size override
            Smin = 0.0;
            Smax = 0.0;
            int row= Setting.Nozzles_current - 1;

            if (NozzlesParameters_dataGridView.Rows[row].Cells["NozzleMinSize_column"].Value == null)
            {
                DisplayText("Bad data at Nozzles vision parameters table, nozzle "
                        + row.ToString(CultureInfo.InvariantCulture) + ", Min. size", KnownColor.DarkRed, true);
                return false;
            }
            if (!double.TryParse(NozzlesParameters_dataGridView.Rows[row].Cells["NozzleMinSize_column"].Value.ToString().Replace(',', '.'), out Smin))
            {
                DisplayText("Bad data at Nozzles vision parameters table, nozzle "
                        + row.ToString(CultureInfo.InvariantCulture) + ", Min. size", KnownColor.DarkRed, true);
                return false;
            }
            if (NozzlesParameters_dataGridView.Rows[row].Cells["NozzleMaxSize_column"].Value == null)
            {
                DisplayText("Bad data at Nozzles vision parameters table, nozzle "
                        + row.ToString(CultureInfo.InvariantCulture) + ", Max. size", KnownColor.DarkRed, true);
                return false;
            }
            if (!double.TryParse(NozzlesParameters_dataGridView.Rows[row].Cells["NozzleMaxSize_column"].Value.ToString().Replace(',', '.'), out Smax))
            {
                DisplayText("Bad data at Nozzles vision parameters table, nozzle "
                        + row.ToString(CultureInfo.InvariantCulture) + ", Max. size", KnownColor.DarkRed, true);
                return false;
            }
            return true;
        }


        public bool CalibrateNozzle_m()
        {
            // Nozzle already loaded
            if (Setting.Placement_OmitNozzleCalibration)
            {
                DisplayText("Nozzle calibration asked, but disabled.");
                return true;
            }

            if (Setting.Nozzles_current == 0)
            {
                DisplayText("Nozzle calibration asked, but no nozzle is loaded.");
                return false;
            }

            DisplayText("calibrating nozzle " + Setting.Nozzles_current);
            // Invalidate the calibration:
            Nozzle.NozzleDataAllNozzles[Setting.Nozzles_current - 1].Calibrated = false;
            NozzlesParameters_dataGridView.Rows[Setting.Nozzles_current - 1].Cells["NozzleCalibrated_Column"].Value = false;
            Update_GridView(NozzlesParameters_dataGridView);

            // find the algorithm:            
            VideoAlgorithmsCollection.FullAlgorithmDescription Alg = new VideoAlgorithmsCollection.FullAlgorithmDescription();
            string AlgName = NozzlesParameters_dataGridView.Rows[Setting.Nozzles_current - 1].Cells["VisionAlgorithm_column"].Value.ToString();
            if (!VideoAlgorithms.FindAlgorithm(AlgName, out Alg))
            {
                DisplayText("*** Calibration algorithm not found!", KnownColor.Red, true);
                DisplayText("*** Set it at Vision Parameters table, Vision Algorith column  at Setup Nozzles page.", KnownColor.Red, true);
                return false;
            }
            UpCamera.BuildMeasurementFunctionsList(Alg.FunctionList);
            UpCamera.MeasurementParameters = Alg.MeasurementParameters;

            // Override size:
            double SmaxSave = UpCamera.MeasurementParameters.Xmax;
            double SminSave = UpCamera.MeasurementParameters.Xmin;
            double YmaxSave = UpCamera.MeasurementParameters.Ymax;
            double YminSave = UpCamera.MeasurementParameters.Ymin;
            bool SizeOverride = false;
            double Smin = 0.0;
            double Smax = 0.0;


            DataGridViewCheckBoxCell cell = NozzlesParameters_dataGridView.Rows[Setting.Nozzles_current - 1].Cells
                ["NozzleOverrideSize_column"] as DataGridViewCheckBoxCell;
            if (cell.Value != null)
            {
                if (cell.Value.ToString().ToUpperInvariant() == "TRUE")
                {
                    if (!CheckSizeData_m(out Smin, out Smax))
                    {
                        return false;
                    }
                    SizeOverride = true;
                }
            }

            bool UpCamWasRunning = false;
            if (UpCamera.Active)
            {
                UpCamWasRunning = true;
            }
            SelectCamera(UpCamera);
            if (!UpCamera.IsRunning())
            {
                ShowMessageBox(
                    "Up camera not running, can't calibrate Nozzle.",
                    "Nozzle calibration failed.",
                    MessageBoxButtons.OK);
                return false;
            }
            // if nozzle isn't already above the up camera:
            if (!(  (Math.Abs(Cnc.CurrentX-Setting.UpCam_PositionX) <0.001) &&
                    (Math.Abs(Cnc.CurrentY - Setting.UpCam_PositionY) < 0.001) &&
                    (Math.Abs(Cnc.CurrentZ - Setting.General_Z0toPCB+0.5) < 0.001)
                    ))
            {
                // take Nozzle there:
                if (!CNC_Z_m(0))
                {
                    return false;
                }

                // take Nozzle to camera
                if (!CNC_XYA_m(Setting.UpCam_PositionX, Setting.UpCam_PositionY, Cnc.CurrentA))
                {
                    return false;
                }
                if (!CNC_Z_m(Setting.General_Z0toPCB - 0.5)) // Average small component height 0.5mm (?)
                {
                    return false;
                }
            }
            // measure the values
            DisplayText("Measuring nozzle " + Setting.Nozzles_current.ToString());
            if (SizeOverride)
            {
                UpCamera.MeasurementParameters.Xmax = Smax;
                UpCamera.MeasurementParameters.Xmin = Smin;
                UpCamera.MeasurementParameters.Ymax = Smax;
                UpCamera.MeasurementParameters.Ymin = Smin;
            }
            if (!CNC_Z_m(Setting.General_Z0toPCB - 0.5)) // Average small component height 0.5mm (?)
            {
                return false;
            }
            if (!Nozzle.Calibrate())
            {
                return false;
            }
            if (SizeOverride)
            {
                UpCamera.MeasurementParameters.Xmax = SmaxSave;
                UpCamera.MeasurementParameters.Xmin = SminSave;
                UpCamera.MeasurementParameters.Ymax = SmaxSave;
                UpCamera.MeasurementParameters.Ymin = SminSave;
            }

            // take Nozzle up
            if (!CNC_Z_m(0))
            {
                return false;
            }

            // UpCamera.PauseProcessing = false;
            if (!UpCamWasRunning)
            {
                SelectCamera(DownCamera);
            }
            NozzleCalibrationClass.CalibrationPointsList Points = Nozzle.NozzleDataAllNozzles[Setting.Nozzles_current - 1].CalibrationPoints;
            for (int i = 0; i < Points.Count; i++)
            {
                DisplayText("A: " + Points[i].Angle.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", X: " + Points[i].X.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", Y: " + Points[i].Y.ToString("0.000", CultureInfo.InvariantCulture));
            }
            Nozzle.NozzleDataAllNozzles[Setting.Nozzles_current - 1].Calibrated = true;
            NozzlesParameters_dataGridView.Rows[Setting.Nozzles_current - 1].Cells["NozzleCalibrated_Column"].Value = true;
            Update_GridView(NozzlesParameters_dataGridView);

            return true;
        }


        private void CalibrateNozzles_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            // this is only called from nozzle tab page, so we want to leave with slack compensation off
            // We want to do moves to camera with slack compensation, if he user has it on

            Nozzles_Stop = false;
            for (int nozzle = 1; nozzle <= Setting.Nozzles_count; nozzle++)
            {
                if (!ChangeNozzle_m(nozzle))
                {
                    return;
                }
                if (Nozzles_Stop)
                {
                    return;
                }
                if (!CalibrateCurrentNozzle())
                {
                    return;
                }
            }
            string path = GetPath();
            Nozzle.SaveNozzlesCalibration(path + NOZZLES_CALIBRATION_DATAFILE);
            for (int nozzle = 1; nozzle <= Setting.Nozzles_count; nozzle++)
            {
                CheckCalibrationErrors(nozzle);
            }
        }

        private bool CalibrateCurrentNozzle()
        {
            if (!CheckPositionConfidence()) return false;

            // We want to do moves to camera with normal speed and slack compensation
            bool SlowSave = Cnc.SlowXY;
            Cnc.SlowXY = false;
            bool compSave = Cnc.SlackCompensation;
            Cnc.SlackCompensation = Setting.CNC_SlackCompensation;
            bool res = CalibrateNozzle_m();
            Cnc.SlowXY = SlowSave;
            Cnc.SlackCompensation = compSave;

            CheckCalibrationErrors(Setting.Nozzles_current);
            return res;
        }

        private void CalibrateThis_button_Click(object sender, EventArgs e)
        {
            CalibrateCurrentNozzle();
            CheckCalibrationErrors(Setting.Nozzles_current);
        }


        private void CalData_button_Click(object sender, EventArgs e)
        {
            DisplayText("Nozzles calibration data:");
            for (int i = 0; i < Setting.Nozzles_count; i++)
            {
                if (Nozzle.NozzleDataAllNozzles[i].Calibrated)
                {
                    DisplayText("Nozzle " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is calibrated:");
                }
                else
                {
                    DisplayText("Nozzle " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is not calibrated:");
                }
                if (Nozzle.NozzleDataAllNozzles[i].CalibrationPoints.Count == 0)
                {
                    DisplayText("No calibration data");
                }
                else
                {
                    foreach (NozzleCalibrationClass.NozzlePoint p in Nozzle.NozzleDataAllNozzles[i].CalibrationPoints)
                    {
                        DisplayText("A: " + p.Angle.ToString("0.000", CultureInfo.InvariantCulture)
                            + ", X: " + p.X.ToString("0.000", CultureInfo.InvariantCulture)
                            + ", Y: " + p.Y.ToString("0.000", CultureInfo.InvariantCulture));
                    }
                }
            }
            DisplayText("Currently used nozzle is " + Setting.Nozzles_current.ToString() + ":");
            if (Setting.Nozzles_current == 0)
            {
                {
                    DisplayText("No nozzle loaded");
                }
            }
            else
            {
                if (!Nozzle.NozzleDataAllNozzles[Setting.Nozzles_current - 1].Calibrated)
                {
                    DisplayText("Not calibrated");
                }
                else
                {
                    DisplayText("Calibration values:");
                    foreach (NozzleCalibrationClass.NozzlePoint p in Nozzle.NozzleDataAllNozzles[Setting.Nozzles_current - 1].CalibrationPoints)
                    {
                        DisplayText("A: " + p.Angle.ToString("0.000", CultureInfo.InvariantCulture)
                            + ", X: " + p.X.ToString("0.000", CultureInfo.InvariantCulture)
                            + ", Y: " + p.Y.ToString("0.000", CultureInfo.InvariantCulture));
                    }
                }
            }
        }


        private void CheckCalibrationErrors(int nozzle)
        {
            double val;
            if (!double.TryParse(NozzleWarning_textBox.Text.Replace(',', '.'), out val))
            {
                DisplayText("Bad data in warning treshold");
                return;
            }
            foreach (NozzleCalibrationClass.NozzlePoint p in Nozzle.NozzleDataAllNozzles[nozzle-1].CalibrationPoints)
            {
                if ((Math.Abs(p.X) > Math.Abs(val)) || (Math.Abs(p.Y) > Math.Abs(val)))
                {
                    DisplayText("WARNING: Calibration value over threshold: ");
                    DisplayText("Nozzle " + nozzle.ToString(CultureInfo.InvariantCulture)
                        + ", A: " + p.Angle.ToString("0.000", CultureInfo.InvariantCulture)
                        + ", X: " + p.X.ToString("0.000", CultureInfo.InvariantCulture)
                        + ", Y: " + p.Y.ToString("0.000", CultureInfo.InvariantCulture));
                }
            }
        }



        // ==========================================================================================================
        // Nozzle change related
        // ==========================================================================================================

        private bool NozzleDataCheck(DataGridView grid, int nozzle, int col, out double value)
        {
            value = 0.0;
            if (grid.RowCount == 0)
            {
                return false;
            }
            if (grid.Rows[nozzle - 1].Cells[col].Value == null)
            {
                return false;
            }
            if (double.TryParse(grid.Rows[nozzle - 1].Cells[col].Value.ToString().Replace(',', '.'), out value))
            {
                return true;
            }
            return false;
        }

        private bool m_GetNozzleStartCoordinates(DataGridView grid, int nozzle, out double X, out double Y, out double Z)
        {
            X = 0.0;
            Y = 0.0;
            Z = 0.0;
            string op = "move not set";
            if (grid == NozzlesLoad_dataGridView)
            {
                op = "load";
            }
            if (grid == NozzlesUnload_dataGridView)
            {
                op = "unload";
            }
            if (!NozzleDataCheck(grid, nozzle, Nozzledata_StartXColumn, out X))
            {
                DisplayText("Bad data, Start X, " + op + " nozzle #" + nozzle.ToString());
                return false;
            }
            if (!NozzleDataCheck(grid, nozzle, Nozzledata_StartYColumn, out Y))
            {
                DisplayText("Bad data, Start Y, " + op + " nozzle #" + nozzle.ToString());
                return false;
            }
            if (!NozzleDataCheck(grid, nozzle, Nozzledata_StartZColumn, out Z))
            {
                DisplayText("Bad data, Start Z, " + op + " nozzle #" + nozzle.ToString());
                return false;
            }
            return true;
        }

        private void CopyUnloadStartPositionsFromLoadEndPositions()
        {
            double X;
            double Y;
            double Z;
            if (NozzlesLoad_dataGridView.RowCount==0)
            {
                return;
            }
            NozzlesLoad_dataGridView.CurrentCell = NozzlesLoad_dataGridView[0, 0];   // If cursor is on an editable cell, old value may be used.
            NozzlesUnload_dataGridView.CurrentCell = NozzlesUnload_dataGridView[0, 0];

            for (int nozzle = 1; nozzle <= Setting.Nozzles_count; nozzle++)
            {
                if (GetLoadresult(nozzle, out X, out Y, out Z))
                {
                    NozzlesUnload_dataGridView.Rows[nozzle - 1].Cells[Nozzledata_StartXColumn].Value = X.ToString("0.000", CultureInfo.InvariantCulture);
                    NozzlesUnload_dataGridView.Rows[nozzle - 1].Cells[Nozzledata_StartYColumn].Value = Y.ToString("0.000", CultureInfo.InvariantCulture);
                    NozzlesUnload_dataGridView.Rows[nozzle - 1].Cells[Nozzledata_StartZColumn].Value = Z.ToString("0.000", CultureInfo.InvariantCulture);

                }
            }
        }

        private void copyUnloadStartPositionsFromLoadEndPositionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Get unload start positions from load end positions", KnownColor.DarkGreen, true);
            CopyUnloadStartPositionsFromLoadEndPositions();
        }

        // ===============================
        // Helper functions for getUnloadMovesFromLoadMovesToolStripMenuItem_Click
        
        private bool FindLastMove_m(int row, out double Z)
        {
            int AxisInd;
            int ValInd;
            Z = 0;
            for (int i = NoOfNozzleMoves; i > 1; i--)
            {
                AxisInd = Nozzledata_StartZColumn + (i - 1) * 2 + 1;
                ValInd= AxisInd+1;
                if (NozzlesLoad_dataGridView.Rows[row].Cells[AxisInd].Value!=null)
                {
                    if (NozzlesLoad_dataGridView.Rows[row].Cells[AxisInd].Value.ToString() != "--")
                    {
                        if (NozzlesLoad_dataGridView.Rows[row].Cells[AxisInd].Value.ToString() != "Z")
                        {
                            DisplayText("last load move should be Z", KnownColor.DarkRed, true);
                            return false;
                        }
                        if (NozzlesLoad_dataGridView.Rows[row].Cells[ValInd].Value == null)
                        {
                            DisplayText("last load move amount not set", KnownColor.DarkRed, true);
                            return false;
                        }
                        if (!double.TryParse(NozzlesLoad_dataGridView.Rows[row].Cells[ValInd].Value.ToString().Replace(',', '.'), out Z))
                        {
                            DisplayText("Bad data: nozzle #" + (row + 1).ToString(CultureInfo.InvariantCulture)
                                + ", move " + i.ToString(CultureInfo.InvariantCulture), KnownColor.DarkRed, true);
                            return false;
                        }
                        return true;
                    }
                }
            }
            DisplayText("Moves to load nozzle " + (row + 1).ToString(CultureInfo.InvariantCulture) + " not set", KnownColor.DarkRed, true);
            return false;
        }


        private void getUnloadMovesFromLoadMovesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Get unload moves from load moves", KnownColor.DarkGreen, true);
            CopyUnloadStartPositionsFromLoadEndPositions();
            for (int i = 0; i < Setting.Nozzles_count; i++)
            {
                NozzlesUnload_dataGridView.Rows[i].Cells[4].Value = "Z";    // first move axis
                double Z;
                if (!FindLastMove_m(i, out Z))
                {
                    return;
                }
                NozzlesUnload_dataGridView.Rows[i].Cells[5].Value = (-Z).ToString("0.000", CultureInfo.InvariantCulture);

                double Yload;
                double Yunload;
                double Xload;
                double Xunload;
                if (!double.TryParse(NozzlesLoad_dataGridView.Rows[i].Cells[Nozzledata_StartYColumn].Value.ToString().Replace(',', '.'), out Yload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString(CultureInfo.InvariantCulture) + ", Load start Y", KnownColor.DarkRed, true);
                    return;
                }
                if (!double.TryParse(NozzlesUnload_dataGridView.Rows[i].Cells[Nozzledata_StartYColumn].Value.ToString().Replace(',', '.'), out Yunload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString(CultureInfo.InvariantCulture) + ", Unload start Y", KnownColor.DarkRed, true);
                    return;
                }
                if (!double.TryParse(NozzlesLoad_dataGridView.Rows[i].Cells[Nozzledata_StartXColumn].Value.ToString().Replace(',', '.'), out Xload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString(CultureInfo.InvariantCulture) + ", Load start X", KnownColor.DarkRed, true);
                    return;
                }
                if (!double.TryParse(NozzlesUnload_dataGridView.Rows[i].Cells[Nozzledata_StartXColumn].Value.ToString().Replace(',', '.'), out Xunload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString(CultureInfo.InvariantCulture) + ", Unload start X", KnownColor.DarkRed, true);
                    return;
                }
                if ((Math.Abs(Yload - Yunload)>0.1)&& (Math.Abs(Xload - Xunload) > 0.1))
                {
                    DisplayText("Both X and Y changed on load sequence; too complex to figure out unload", KnownColor.DarkRed, true);
                    return;
                }
                if ((Math.Abs(Yload - Yunload) > 0.1))
                {
                    NozzlesUnload_dataGridView.Rows[i].Cells[6].Value = "Y";
                    NozzlesUnload_dataGridView.Rows[i].Cells[7].Value = (Yload - Yunload).ToString("0.000", CultureInfo.InvariantCulture);
                }
                else
                {
                    NozzlesUnload_dataGridView.Rows[i].Cells[6].Value = "X";
                    NozzlesUnload_dataGridView.Rows[i].Cells[7].Value = (Xload - Xunload).ToString("0.000", CultureInfo.InvariantCulture);
                }
                NozzlesUnload_dataGridView.Rows[i].Cells[8].Value = "Z";
                NozzlesUnload_dataGridView.Rows[i].Cells[9].Value = Z.ToString("0.000", CultureInfo.InvariantCulture);
            }
        }

    // ==========================================================================================================
    private void copyLoadMovesFromNozzle1_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Copy load moves from nozzle 1", KnownColor.DarkGreen);
            copyMovesFromNozzle1(NozzlesLoad_dataGridView);
        }

        private void copyUnloadMovesFromNozzle1_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Copy unload moves from nozzle 1", KnownColor.DarkGreen);
            copyMovesFromNozzle1(NozzlesUnload_dataGridView);
        }

        private void copyMovesFromNozzle1(DataGridView grid)
        {
            if (grid.RowCount==0)
            {
                return;
            }
            grid.CurrentCell = grid[0, 0];
            for (int nozzle = 1; nozzle < Setting.Nozzles_count; nozzle++)
            {
                for (int i = Nozzledata_StartZColumn+1; i < grid.ColumnCount; i++)
                {
                    grid.Rows[nozzle].Cells[i].Value = grid.Rows[0].Cells[i].Value;
                }
            }
        }

        // ==========================================================================================================
        private bool GetLoadresult(int nozzle, out double X, out double Y, out double Z)
        {
            X = 0.0;
            Y = 0.0;
            Z = 0.0;
            if (!m_GetNozzleStartCoordinates(NozzlesLoad_dataGridView, nozzle, out X, out Y, out Z))
            {
                return false;
            }
            int Move = 1;
            bool AllDone = false;
            double dX;
            double dY;
            double dZ;
            while (!AllDone)
            {
                if (!m_getNozzleMove(NozzlesLoad_dataGridView, nozzle, Move++, out dX, out dY, out dZ, out AllDone))
                {
                    return false;
                }
                X = X + dX;
                Y = Y + dY;
                Z = Z + dZ;
            }
            return true;
        }

        private bool m_getNozzleMove(DataGridView grid, int nozzle, int move, out double dX, out double dY, out double dZ, out bool AllDone)
        {
            dX = 0.0;
            dY = 0.0;
            dZ = 0.0;
            AllDone = false;
            if (move > NoOfNozzleMoves)
            {
                AllDone = true;
                return false;
            };
            // is direction set? If not, all done.
            int DirCol = Nozzledata_StartZColumn + (move - 1) * 2 + 1;
            if (grid.Rows[nozzle - 1].Cells[DirCol].Value == null)
            {
                AllDone = true;
                return true;
            }
            if (grid.Rows[nozzle - 1].Cells[DirCol].Value.ToString() == "--")
            {
                AllDone = true;
                return true;
            }
            double val;
            string op= "undefined";
            if (grid == NozzlesLoad_dataGridView)
            {
                op = "load ";
            }
            if (grid == NozzlesUnload_dataGridView)
            {
                op = "unload ";
            }
            if (!NozzleDataCheck(grid, nozzle, DirCol + 1, out val))
            {
                DisplayText("Bad data: " + op + "nozzle #" + nozzle + ", move " + (DirCol + 1).ToString(CultureInfo.InvariantCulture), KnownColor.DarkRed, true);
            }
            switch (grid.Rows[nozzle - 1].Cells[DirCol].Value.ToString())
            {
                case "X":
                    dX = val;
                    DisplayText(op + " nozzle " + nozzle.ToString(CultureInfo.InvariantCulture)
                        + ", move " + move.ToString(CultureInfo.InvariantCulture) + ": X" + val.ToString(CultureInfo.InvariantCulture));
                    break;

                case "Y":
                    dY = val;
                    DisplayText(op + " nozzle " + nozzle.ToString(CultureInfo.InvariantCulture)
                        + ", move " + move.ToString(CultureInfo.InvariantCulture) + ": Y" + val.ToString(CultureInfo.InvariantCulture));
                    break;

                case "Z":
                    dZ = val;
                    DisplayText(op + " nozzle " + nozzle.ToString(CultureInfo.InvariantCulture)
                        + ", move " + move.ToString(CultureInfo.InvariantCulture) + ": Z" + val.ToString(CultureInfo.InvariantCulture));
                    break;

                default:
                    DisplayText("m_getNozzleMove: " + op + " nozzle" + nozzle.ToString(CultureInfo.InvariantCulture)
                        + ", move " + move.ToString(CultureInfo.InvariantCulture) + "?");
                    return false;
                    //break;
            }
            return true;
        }

        
        private void GetCoordinates_button_Click(DataGridView grid)
        {
            // Use story: The user is setting up the nozles. Jog nozzle holder to start position,
            // click get, start position is automatically filled and the next step box is selected.
            // Jog the next step and click get, the axis and move size are automatically filled
            if (grid.RowCount==0)
            {
                return;
            }
            int row = grid.CurrentCell.RowIndex;
            int col = grid.CurrentCell.ColumnIndex;
            DisplayText("Current Cell: " + row.ToString() + ", " + col.ToString(CultureInfo.InvariantCulture));

            if (col <= Nozzledata_StartZColumn)
            {
                grid.Rows[row].Cells[Nozzledata_StartXColumn].Value = Cnc.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
                grid.Rows[row].Cells[Nozzledata_StartYColumn].Value = Cnc.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
                grid.Rows[row].Cells[Nozzledata_StartZColumn].Value = Cnc.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture);
                grid.CurrentCell = grid.Rows[row].Cells[Nozzledata_StartZColumn + 2];
            }
            else
            {
                int MoveNo = (col - Nozzledata_StartZColumn) / 2;
                DisplayText("move no " + MoveNo.ToString(CultureInfo.InvariantCulture));
                // Get coordinates until the move
                // Start position
                double X; 
                double Y;
                double Z;
                if (!m_GetNozzleStartCoordinates(grid, row+1, out X, out Y, out Z))
                {
                    return;
                }
                // Follow the moves until before the current move
                for (int move = 1; move < MoveNo; move++)
                {
                    double amount;
                    int amountCol = 2 * move + Nozzledata_StartZColumn;
                    int dirCol = 2 * move + Nozzledata_StartZColumn-1;
                    if (!NozzleDataCheck(grid, row+1, amountCol, out amount))
                    {
                        DisplayText("Bad data, move " + move.ToString(CultureInfo.InvariantCulture) + " amount", KnownColor.DarkRed, true);
                        return;
                    }
                    // direction
                    if (grid.Rows[row].Cells[dirCol].Value == null)
                    {
                        DisplayText("Direction not set at move " + MoveNo.ToString(CultureInfo.InvariantCulture), KnownColor.DarkRed, true);
                        return;
                    }
                    else if (grid.Rows[row].Cells[dirCol].Value.ToString() == "--")
                    {
                        DisplayText("Direction not set at move " + MoveNo.ToString(CultureInfo.InvariantCulture), KnownColor.DarkRed, true);
                        return;
                    }
                    else
                    {
                        if (grid.Rows[row].Cells[dirCol].Value.ToString() == "X")
                        {
                            X += amount;
                        }
                        else if (grid.Rows[row].Cells[dirCol].Value.ToString() == "Y")
                        {
                            Y += amount;
                        }
                        else
                        {
                            Z += amount;
                        }
                    }
                }
                DisplayText("Position until here: X= "
                    + X.ToString(CultureInfo.InvariantCulture)
                    + ", Y= " + Y.ToString(CultureInfo.InvariantCulture)
                    + ", Z= " + Z.ToString(CultureInfo.InvariantCulture), KnownColor.Blue);
                // get current position
                // check that one but only one coordinate has changed
                int count = 0;
                if (Math.Abs(X - Cnc.CurrentX) > 0.01)
                {
                    DisplayText("X changed");
                    count++;
                }
                if (Math.Abs(Y - Cnc.CurrentY) > 0.01)
                {
                    DisplayText("Y changed");
                    count++;
                }
                if (Math.Abs(Z - Cnc.CurrentZ) > 0.01)
                {
                    DisplayText("Z changed");
                    count++;
                }
                if (count == 0)
                {
                    DisplayText("No moves, previous steps would take the machine here.", KnownColor.DarkRed, true);
                    return;
                }
                if (count != 1)
                {
                    DisplayText("More than one coordinate changed from where previous steps would take the machine.", KnownColor.DarkRed, true);
                    return;
                }
                // All ok, set direction and value
                int amCol = 2 * MoveNo + Nozzledata_StartZColumn;
                int dCol = 2 * MoveNo + Nozzledata_StartZColumn-1;
                int nextcol=2 * MoveNo + Nozzledata_StartZColumn + 2;
                if (nextcol<grid.ColumnCount)
                {
                    grid.CurrentCell = grid.Rows[row].Cells[2 * MoveNo + Nozzledata_StartZColumn + 2];
                }
                if (Math.Abs(X - Cnc.CurrentX) > 0.01)
                {
                    grid.Rows[row].Cells[dCol].Value = "X";
                    grid.Rows[row].Cells[amCol].Value = (Cnc.CurrentX - X).ToString(CultureInfo.InvariantCulture);
                    return;
                }
                if (Math.Abs(Y - Cnc.CurrentY) > 0.01)
                {
                    grid.Rows[row].Cells[dCol].Value = "Y";
                    grid.Rows[row].Cells[amCol].Value = (Cnc.CurrentY - Y).ToString(CultureInfo.InvariantCulture);
                    return;
                }
                grid.Rows[row].Cells[dCol].Value = "Z";
                grid.Rows[row].Cells[amCol].Value = (Cnc.CurrentZ - Z).ToString(CultureInfo.InvariantCulture);
                return;
            }
        }

        private void GetLoadCoordinates_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            GetCoordinates_button_Click(NozzlesLoad_dataGridView);
        }

        private void GetUnloadCoordinates_button_Click(object sender, EventArgs e)
        {
            if (!CheckPositionConfidence()) return;

            GetCoordinates_button_Click(NozzlesUnload_dataGridView);
        }

        // ==========================================================================================================
        // load / unload nozzles, button handlers
        // ==========================================================================================================
        private void SetDefaultNozzle_button_Click(object sender, EventArgs e)
        {
            Setting.Nozzles_default = (int)ForceNozzle_numericUpDown.Value;
            DefaultNozzle_label.Text = Setting.Nozzles_default.ToString(CultureInfo.InvariantCulture);
        }

        private void ForceNozzleStatus_button_Click(object sender, EventArgs e)
        {
            if (ForceNozzle_numericUpDown.Value==0)
            {
                NozzleNo_textBox.Text = "--";
            }
            else
            {
                NozzleNo_textBox.Text = ForceNozzle_numericUpDown.Value.ToString(CultureInfo.InvariantCulture);
            }
            Setting.Nozzles_current = (int)ForceNozzle_numericUpDown.Value;
        }

        private bool m_UnloadNozzle(int Nozzle)
        {
            DisplayText("Unload nozzle #" + Nozzle.ToString(CultureInfo.InvariantCulture), KnownColor.Blue);
            if (m_DoNozzleSequence(NozzlesUnload_dataGridView, Nozzle))
            {
                NozzleNo_textBox.Text = "--";
                Setting.Nozzles_current = 0;
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool m_LoadNozzle(int NozzleNo)
        {
            DisplayText("Load nozzle #" + NozzleNo.ToString(CultureInfo.InvariantCulture), KnownColor.Blue);
            if (m_DoNozzleSequence(NozzlesLoad_dataGridView, NozzleNo))
            {
                NozzleNo_textBox.Text = NozzleNo.ToString(CultureInfo.InvariantCulture);
                Setting.Nozzles_current = NozzleNo;
                return true;
            }
            else
            {
                return false;
            }
        }

        private void ChangeNozzle_button_Click(object sender, EventArgs e)
        {
            ChangeNozzle_m((int)ForceNozzle_numericUpDown.Value);
        }

        // ==========================================================================================================
        // Change nozzle: This routine does the magic
        // ==========================================================================================================

        public bool ChangeNozzle_m(int Nozzle)
        {

            if (!PositionConfidence)
            {
                DisplayText("Nozzle change: Machine position is lost.", KnownColor.DarkRed, true);
                return false;
            }

            Nozzles_Stop = false;
            if (Nozzle == Setting.Nozzles_current)
            {
                DisplayText("Wanted nozzle (#" + Nozzle.ToString(CultureInfo.InvariantCulture) + ") already loaded");
                return true;
            };

            // store cnc speed settings
            bool slowXY = Cnc.SlowXY;
            bool slowZ = Cnc.SlowZ;
            bool slowA = Cnc.SlowA;
            double XYspeed = Cnc.NozzleSpeedXY;
            double Zspeed = Cnc.NozzleSpeedZ;
            double Aspeed = Cnc.NozzleSpeedA;
            int timeout = CNC_timeout;

            // replace with nozzle speed settings
            Cnc.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Cnc.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Cnc.SlowA = !Setting.Nozzles_AfullSpeed;
            Cnc.NozzleSpeedXY = Setting.Nozzles_XYspeed;
            Cnc.NozzleSpeedZ = Setting.Nozzles_Zspeed;
            Cnc.NozzleSpeedA = Setting.Nozzles_Aspeed;
            CNC_timeout = Setting.Nozzles_Timeout;

            bool ok = true;
            // disable z switches 
            ZGuardOff();
            Cnc.DisableZswitches();

            // Unload if needed
            if (Setting.Nozzles_current != 0)
            {
                if (!m_UnloadNozzle(Setting.Nozzles_current))
                {
                    ShowMessageBox(
                        "Nozzle unload failed, check situation and log window.",
                        "Nozzle unload failed",
                        MessageBoxButtons.OK);
                    ok = false;
                }
            }
            // Load if needed
            if ((Nozzle != 0) && ok)
            {
                if (!m_LoadNozzle(Nozzle))
                {
                    ShowMessageBox(
                        "Nozzle load failed, check situation and log window.",
                        "Nozzle load failed",
                        MessageBoxButtons.OK);
                    ok = false;
                }
            }

            if (!AtNozzlesTab)
            {
                ZGuardOn();
                Cnc.EnableZswitches();
            }

            // restore cnc speed settings
            Cnc.SlowXY = slowXY;
            Cnc.SlowZ = slowZ;
            Cnc.SlowA = slowA;
            Cnc.NozzleSpeedXY = XYspeed;
            Cnc.NozzleSpeedZ = Zspeed;
            Cnc.NozzleSpeedA = Aspeed;
            CNC_timeout = timeout;

            return ok;
        }


        // ==========================================================================================================
        // actual moves
        private void GotoZ0_button_Click(object sender, EventArgs e)
        {
            CNC_Z_m(0.0);
        }


        private bool m_NozzleGotoStart(DataGridView grid, int nozzle)
        {
            if (!CheckPositionConfidence()) return false;

            double X;
            double Y;
            double Z;

            DisplayText("m_NozzleGotoStart: nozzle #" + nozzle.ToString(), KnownColor.DarkBlue, true);
            if (!m_GetNozzleStartCoordinates(grid, nozzle, out X, out Y, out Z))
            {
                return false;
            }
            if (Setting.Nozzles_FirstMoveFullSpeed)
            {
                Cnc.SlowXY = false;
                Cnc.SlowZ = false;
                Cnc.SlowA = false;
            }
            if (!CNC_Z_m(0.0))
            {
                return false;
            }
            if (Setting.Nozzles_FirstMoveSlackCompensation)
            {
                if (!CNC_XYA_m(X - Setting.SlackCompensationDistance, Y - Setting.SlackCompensationDistance, -5.0))
                {
                    return false;
                }
            }
            if (!CNC_XYA_m(X, Y, 0.0))
            {
                return false;
            }
            if (!CNC_Z_m(Z))
            {
                return false;
            }
            Cnc.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Cnc.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Cnc.SlowA = !Setting.Nozzles_AfullSpeed;

            return true;
        }

        private bool m_DoNozzleSequence(DataGridView grid, int Nozzle)
        {
            bool slack = Cnc.SlackCompensation;  // this routine can be called outside nozzle tab, so we need to store slack values, ...
            bool slackA = Cnc.SlackCompensationA;
            Cnc.SlackCompensation = false;      // .. but we don't want slack compensation moves on nozzle load/unload
            Cnc.SlackCompensationA = false;

            if (Nozzles_Stop)
            {
                return false;
            }
            m_NozzleGotoStart(grid, Nozzle);

            int Move = 1;
            bool AllDone = false;
            while (!AllDone)
            {
                if (Nozzles_Stop)
                {
                    Cnc.SlackCompensation = slack;
                    Cnc.SlackCompensationA = slackA;
                    return false;
                }
                if (!m_DoNozzleMove(grid, Nozzle, Move++, out AllDone))
                {
                    Cnc.SlackCompensation = slack;
                    Cnc.SlackCompensationA = slackA;
                    return false;
                }
            }
            Cnc.SlackCompensation = slack;
            Cnc.SlackCompensationA = slackA;
            return true;
        }

        // Does one nozzle move, notifies caller if no more moves is needed (AllDone)
        private bool m_DoNozzleMove(DataGridView grid, int nozzle, int MoveNumber,out bool AllDone)
        {
            // Sanity checks
            if (MoveNumber> NoOfNozzleMoves+1)
            {
                DisplayText("Too many moves for nozzle " +  nozzle.ToString()
                    + "attemtpting move no " + MoveNumber.ToString(CultureInfo.InvariantCulture), KnownColor.DarkRed, true);
                AllDone = true;
                return false;
            }
            AllDone = false;
            if (grid.RowCount==0)
            {
                return false;
            }

            // is this the last defined move?
            int DirCol = Nozzledata_StartZColumn + (MoveNumber-1) * 2+1;
            bool LastMove = false;
            if (MoveNumber == NoOfNozzleMoves)
            {
                LastMove = true;
            }
            else if (grid.Rows[nozzle - 1].Cells[DirCol+2].Value == null)
            {
                LastMove = true;
            }
            else if(grid.Rows[nozzle - 1].Cells[DirCol + 2].Value.ToString() == "--")
            {
                LastMove = true;
            }
            // If so and if so set, do it full speed
            if (LastMove && Setting.Nozzles_LastMoveFullSpeed)
            {
                Cnc.SlowXY = false;
                Cnc.SlowZ = false;
                Cnc.SlowA = false;
            }

            // Check that all necessary data in the grid is valid
            double val;
            if (!NozzleDataCheck(grid, nozzle, DirCol + 1, out val))
            {
                string op = "Undefined ";
                if (grid == NozzlesLoad_dataGridView)
                {
                    op = "load ";
                }
                if (grid == NozzlesUnload_dataGridView)
                {
                    op = "unload ";
                }
                DisplayText("Bad data: " + op + "nozzle #" + nozzle + ", move " 
                    + MoveNumber.ToString(CultureInfo.InvariantCulture), KnownColor.DarkRed, true);
            }
            string axis=grid.Rows[nozzle - 1].Cells[DirCol].Value.ToString();


            // Do the move
            DisplayText("m_DoNozzleMove: nozzle #" + nozzle + ", move "
           + MoveNumber.ToString(CultureInfo.InvariantCulture) + ":", KnownColor.DarkBlue, true);
            if (axis == "Z")
            {
                if (!CNC_Z_m(Cnc.CurrentZ + val))
                {
                    return false;
                }
            }
            else
            {
                double X = Cnc.CurrentX;
                double Y = Cnc.CurrentY;
                switch (axis)
                {
                    case "X":
                        X = X + val;
                        break;

                    case "Y":
                        Y = Y + val;
                        break;

                    default:
                        DisplayText("m_DoNozzleMove: nozzle #" + nozzle + ", move "
                            + MoveNumber.ToString(CultureInfo.InvariantCulture) + ", axis?", KnownColor.DarkRed, true);
                        Cnc.SlowXY = !Setting.Nozzles_XYfullSpeed;
                        Cnc.SlowZ = !Setting.Nozzles_ZfullSpeed;
                        Cnc.SlowA = !Setting.Nozzles_AfullSpeed;
                        return false;
                        //break;
                }
                if (!CNC_XYA_m(X, Y, Cnc.CurrentA))
                {
                    return false;
                }
            }
            Cnc.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Cnc.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Cnc.SlowA = !Setting.Nozzles_AfullSpeed;

            if (LastMove)
            {
                AllDone = true;
                return true;
            }
            return true;
        }


        private void NozzleZGuard_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if ( NozzleZGuard_checkBox.Checked)
            {
                ZGuardOff();
            }
            else
            {
                ZGuardOn();
            }
        }

        // ==========================================================================================================
        private void NozzleWarning_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleWarning_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_WarningTreshold = val;
                NozzleWarning_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleWarning_textBox.ForeColor = Color.Red;
            }
        }

        // ==========================================================================================================
        // Speed controls

        private void NozzleXYspeed_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleXYspeed_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_XYspeed = val;
                Cnc.NozzleSpeedXY = val;
                NozzleXYspeed_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleXYspeed_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleZspeed_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleZspeed_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_Zspeed = val;
                Cnc.NozzleSpeedZ = val;
                NozzleZspeed_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleZspeed_textBox.ForeColor = Color.Red;
            }
        }
        private void NozzleAspeed_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleAspeed_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_Aspeed = val;
                Cnc.NozzleSpeedA = val;
                NozzleAspeed_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleAspeed_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleTimeout_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(NozzleTimeout_textBox.Text, out val))
            {
                Setting.Nozzles_Timeout = val;
                CNC_timeout = val;
                NozzleTimeout_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleTimeout_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleXYFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_XYfullSpeed = NozzleXYFullSpeed_checkBox.Checked;
            Cnc.SlowXY = !NozzleXYFullSpeed_checkBox.Checked;
        }

        private void NozzleZFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_ZfullSpeed = NozzleZFullSpeed_checkBox.Checked;
            Cnc.SlowZ = !NozzleZFullSpeed_checkBox.Checked;
        }

        private void NozzleAFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_AfullSpeed = NozzleAFullSpeed_checkBox.Checked;
            Cnc.SlowA = !NozzleAFullSpeed_checkBox.Checked;
        }

        private void Nozzle1stMoveSlackComp_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_FirstMoveSlackCompensation = Nozzle1stMoveSlackComp_checkBox.Checked;
        }

        private void FirstMoveFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_FirstMoveFullSpeed = FirstMoveFullSpeed_checkBox.Checked;
        }

        private void LastMoveFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_LastMoveFullSpeed = LastMoveFullSpeed_checkBox.Checked;
        }

        private void NozzleChangeEnable_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (Setting.Nozzles_count == 0)
            {
                NozzleChangeEnable_checkBox.Checked = false;
            }
            Setting.Nozzles_Enabled = NozzleChangeEnable_checkBox.Checked;
        }

        private void SetNoOfNozzles_button_Click(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            UpdateNozzlesGridSize();
        }

         public void UpdateNozzlesGridSize()
        {
            if (NoOfNozzles_UpDown.Value > Setting.Nozzles_maximum)
            {
                NoOfNozzles_UpDown.Value = Setting.Nozzles_maximum;
            }
            Setting.Nozzles_count = (int)NoOfNozzles_UpDown.Value;
            ForceNozzle_numericUpDown.Maximum = Setting.Nozzles_count;
            NoOfNozzlesOnVideoSetup_numericUpDown.Maximum = Setting.Nozzles_count;
            while (NoOfNozzles_UpDown.Value > NozzlesLoad_dataGridView.RowCount)
            {
                AddNozzle();
            }
            while (NoOfNozzles_UpDown.Value < NozzlesLoad_dataGridView.RowCount)
            {
                if (NozzlesLoad_dataGridView.RowCount > 0)
                {
                    RemoveNozzle();
                }
            }

            while (NozzlesParameters_dataGridView.Rows.Count > Setting.Nozzles_count)
            {
                NozzlesParameters_dataGridView.Rows.RemoveAt(NozzlesParameters_dataGridView.Rows.Count - 1);
            }

            while (NozzlesParameters_dataGridView.Rows.Count < Setting.Nozzles_count)
            {
                NozzlesParameters_dataGridView.Rows.Add();
            }

            if (Setting.Nozzles_count == 0)
            {
                NozzleChangeEnable_checkBox.Checked = false;
            }
        }
        #endregion

        // ==========================================================================================================
        #region ApplicationSettings

        private void AppSettingsSave_button_Click(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            string path = GetPath();
            AppSettings_saveFileDialog.Filter = "All files (*.*)|*.*";
            AppSettings_saveFileDialog.FileName = APPLICATIONSETTINGS_DATAFILE;
            AppSettings_saveFileDialog.InitialDirectory = path;

            if (AppSettings_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                Setting.Save(Setting, AppSettings_saveFileDialog.FileName);
            }

        }

        private void AppSettingsLoad_button_Click(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            DialogResult dialogResult = ShowMessageBox(
                "New set of settings will be loaded and the program will close. You need to restart manually.\n\rContinue?",
                "Confirm loading new settings", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.No)
            {
                return;
            };
            string path = GetPath();
            AppSettings_openFileDialog.Filter = "All files (*.*)|*.*";
            AppSettings_openFileDialog.FileName = APPLICATIONSETTINGS_DATAFILE;
            AppSettings_openFileDialog.InitialDirectory = path;

            if (AppSettings_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                Setting = Setting.Load(AppSettings_openFileDialog.FileName);
                Application.Restart();
            }
        }

        private void AppBuiltInSettings_button_Click(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            DialogResult dialogResult = ShowMessageBox(
                "Settings will reset to defaults and the program will close. You need to restart manually.\n\rContinue?",
                "Confirm Loading Built-In settings", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.No)
            {
                return;
            };
            Setting = new MySettings(); 
            Setting.MainForm = this;
            Application.Exit();
        }
        #endregion


        // ==========================================================================================================
        #region BoardSettings

        private void BoardSettingsSave_button_Click(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            string path = GetPath();
            AppSettings_saveFileDialog.Filter = "LitePlacer datafiles (LitePlacer.*)|LitePlacer.*|All files (*.*)|*.*";
            AppSettings_saveFileDialog.FileName = BOARDSETTINGS_DATAFILE;
            AppSettings_saveFileDialog.InitialDirectory = path;

            if (Setting.Controlboard == ControlBoardType.TinyG)
            {

                if (AppSettings_saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    SaveTinyGSettings(TinyGBoard, AppSettings_saveFileDialog.FileName);
                }
                return;
            }
            MainForm.DisplayText("*** Skipping saving board settings file; board type unknown or unsupported");
        }


        private void BoardSettingsLoad_button_Click(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            string path = GetPath();
            AppSettings_openFileDialog.Filter = "LitePlacer datafiles (LitePlacer.*)|LitePlacer.*|All files (*.*)|*.*";
            AppSettings_openFileDialog.FileName = BOARDSETTINGS_DATAFILE;
            AppSettings_openFileDialog.InitialDirectory = path;

            if (Setting.Controlboard == ControlBoardType.TinyG)
            {
                TinyGSettings tg = TinyGBoard;
                if (AppSettings_openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (!LoadTinyGsettings(ref tg, AppSettings_openFileDialog.FileName))
                    {
                        return;
                    }
                    TinyGBoard = tg;
                    WriteAllTinyGSettings_m();
                }
                return;
            }
            DisplayText("*** Skipping loading board settings operation; board type unknown or unsupported");
        }


        private void BoardBuiltInSettings_button_Click(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            string path = GetPath();
            if (Setting.Controlboard == ControlBoardType.TinyG)
            {
                TinyGBoard = new TinyGSettings();
                WriteAllTinyGSettings_m();
                return;
            }
            DisplayText("*** Skipping loading board settings operation; board type unknown or unsupported");
        }

        #endregion

        // =========================================================================
        // TODO: Move routines below to correct places

        public bool DownCameraRotationFollowsA { get; set; } = false;
        private void apos_textBox_TextChanged(object sender, EventArgs e)
        {
            if (DownCameraRotationFollowsA)
            {
                DownCamera.BoxRotationDeg = Cnc.CurrentA;
            }
        }

        private void DownCamListResolutions_button_Click(object sender, EventArgs e)
        {
            List<string> Monikers = DownCamera.GetMonikerStrings();
            if (Monikers==null)
            {
                DisplayText("Could not get camera indentifier string.", KnownColor.Purple, true);
                return;
            }
            if (DownCam_comboBox.SelectedIndex < 0)
            {
                DisplayText("No camera selected in the drop down box.", KnownColor.Purple, true);
                return;
            }
            if (DownCam_comboBox.SelectedIndex >= DownCam_comboBox.Items.Count)
            {
                DisplayText("Camera no longer there (click refresh list).", KnownColor.Purple, true);
                return;
            }
            string MonikerStr = Monikers[DownCam_comboBox.SelectedIndex];
            DownCamera.GetResolutions(MonikerStr);
        }

        private void UpCamListResolutions_button_Click(object sender, EventArgs e)
        {
            List<string> Monikers = UpCamera.GetMonikerStrings();
            if (Monikers == null)
            {
                DisplayText("Could not get camera indentifier string.", KnownColor.Purple, true);
                return;
            }
            if (UpCam_comboBox.SelectedIndex < 0)
            {
                DisplayText("No camera selected in the drop down box.");
                return;
            }
            if (UpCam_comboBox.SelectedIndex >= UpCam_comboBox.Items.Count)
            {
                DisplayText("Camera no longer there (click refresh list).", KnownColor.Purple, true);
                return;
            }
            string MonikerStr = Monikers[UpCam_comboBox.SelectedIndex-1];
            UpCamera.GetResolutions(MonikerStr);
        }


        private void DownCameraDesiredX_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(DownCameraDesiredX_textBox.Text, out val))
            {
                DownCameraDesiredX_textBox.ForeColor = Color.Black;
                DownCamera.DesiredResolutionX = val;
                Setting.DownCam_DesiredX = val;
            }
            else
            {
                DownCameraDesiredX_textBox.ForeColor = Color.Red;
            }
        }

        private void DownCameraDesiredY_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(DownCameraDesiredY_textBox.Text, out val))
            {
                DownCameraDesiredY_textBox.ForeColor = Color.Black;
                DownCamera.DesiredResolutionY = val;
                Setting.DownCam_DesiredY = val;
            }
            else
            {
                DownCameraDesiredY_textBox.ForeColor = Color.Red;
            }
        }

        private void UpCameraDesiredX_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(UpCameraDesiredX_textBox.Text, out val))
            {
                UpCameraDesiredX_textBox.ForeColor = Color.Black;
                UpCamera.DesiredResolutionX = val;
                Setting.UpCam_DesiredX = val;
            }
            else
            {
                UpCameraDesiredX_textBox.ForeColor = Color.Red;
            }
        }

        private void UpCameraDesiredY_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(UpCameraDesiredY_textBox.Text, out val))
            {
                UpCameraDesiredY_textBox.ForeColor = Color.Black;
                UpCamera.DesiredResolutionY = val;
                Setting.UpCam_DesiredY = val;
            }
            else
            {
                UpCameraDesiredY_textBox.ForeColor = Color.Red;
            }
        }


        private void VigorousHoming_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.General_VigorousHoming = VigorousHoming_checkBox.Checked;
        }

        private void Ato0_button_Click(object sender, EventArgs e)
        {
            Cnc.SetPosition(X: "", Y: "", Z: "", A: "0");
        }

        private void MeasureAndSet_button_Click(object sender, EventArgs e)
        {
            if (MovementIsBusy_m())
            {
                return;
            }
            if (!CheckPositionConfidence()) return;

            if (!CNC_XYA_m(0.0, 0.0, Cnc.CurrentA))
            {
                return;
            }
            OpticalHoming_m();
        }

        private void MoveTimeout_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(MoveTimeout_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.CNC_RegularMoveTimeout = val;
                Cnc.RegularMoveTimeout = val;
            }
        }

        private void SlackCompensationDistance_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(SlackCompensationDistance_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.SlackCompensationDistance = val;
                Cnc.SlackCompensationDistance = val;
            }
        }

        private void VideoProcessingZguard_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (VideoProcessingZguard_checkBox.Checked)
            {
                ZGuardOff();
            }
            else
            {
                ZGuardOn();
            }

        }

        private void ChangeNozzleOnVideoSetup_button_Click(object sender, EventArgs e)
        {
            ChangeNozzle_m((int)NoOfNozzlesOnVideoSetup_numericUpDown.Value);
        }

        private void CalibrateNozzleOnVideoSetup_button_Click(object sender, EventArgs e)
        {
            CalibrateThis_button_Click(sender, e);      // does the same as the similar button on nozzle setup tab
        }

        private void HideAdvanced_tabPage_Enter(object sender, EventArgs e)
        {
            SpecialProcessing_button.Visible = true;
            AdvancedProcessing_tabControl.Visible = false;
        }

        private void SpecialProcessing_button_Click(object sender, EventArgs e)
        {
            SpecialProcessing_button.Visible = false;
            AdvancedProcessing_tabControl.SelectedTab = NozzleCalibration_tabPage;
            AdvancedProcessing_tabControl.Visible = true;
            EnterStoredImagetab();
        }

        private void AutoPark_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.General_Autopark = AutoPark_checkBox.Checked;
        }

        private void OptimizeA_checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            Setting.CNC_OptimizeA = OptimizeA_TinyG_checkBox.Checked;

        }

        private void OptimizeA_checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            Setting.CNC_OptimizeA = OptimizeA_Marlin_checkBox.Checked;
        }

        private void HoleTest_maskedTextBox_TextChanged(object sender, EventArgs e)
        {
            int val = 1;
            if (!int.TryParse(HoleTest_maskedTextBox.Text, out val))
            {
                HoleTest_maskedTextBox.ForeColor = Color.Red;
                return;
            }
            if (val < 1)
            {
                HoleTest_maskedTextBox.ForeColor = Color.Red;
                return;
            }
            HoleTest_maskedTextBox.ForeColor = Color.Black;
        }

        private void TapeSetupZguard_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (TapeSetupZguard_checkBox.Checked)
            {
                ZGuardOff();
            }
            else
            {
                ZGuardOn();
            }
        }

        private void SaveNozzleCalibration_button_Click(object sender, EventArgs e)
        {
            string path = GetPath();
            Nozzle.SaveNozzlesCalibration(path + NOZZLES_CALIBRATION_DATAFILE);
        }

        private void DownCameraDesiredX_textBox_Enter(object sender, EventArgs e)
        {
            DownCameraDesiredX_textBox.SelectAll();
        }

        private void DownCameraDesiredY_textBox_Enter(object sender, EventArgs e)
        {
            DownCameraDesiredY_textBox.SelectAll();
        }

        private void UpCameraDesiredX_textBox_Enter(object sender, EventArgs e)
        {
            UpCameraDesiredX_textBox.SelectAll();
        }

        private void UpCameraDesiredY_textBox_Enter(object sender, EventArgs e)
        {
            UpCameraDesiredY_textBox.SelectAll();
        }

        private void CameraSetupTest_button_Click(object sender, EventArgs e)
        {
        }

        private void Invoke_CameraPropertiesDialog(Camera cam)
        {
            DisplayText("Open camera properties dialog", KnownColor.DarkGreen, true);
            CameraProperties CameraPropertiesDialog = new CameraProperties(this, cam);
            CameraPropertiesDialog.Show(this);
        }

        private void DownCamProperties_button_Click(object sender, EventArgs e)
        {
            if (!DownCamera.IsRunning())
            {
                DisplayText("Downcam not running");
                return;
            }
            Invoke_CameraPropertiesDialog(DownCamera);
        }

        private void UpCamProperties_button_Click(object sender, EventArgs e)
        {
            if (!UpCamera.IsRunning())
            {
                DisplayText("Upcam not running");
                return;
            }
            Invoke_CameraPropertiesDialog(UpCamera);
        }

        private void NumPadJog_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.CNC_EnableNumPadJog = NumPadJog_checkBox.Checked;
        }

        private void MouseScroll_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.CNC_EnableMouseWheelJog = MouseScroll_checkBox.Checked;
        }

        private void JobData_GridView_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            DisplayText("JobData_GridView_DataError");
        }

        private void NegativeMoveX_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NegativeMoveX_textBox.Text.Replace(',', '.'), out val))
            {
                NegativeMoveX_textBox.ForeColor = Color.Black;
                Setting.General_NegativeX = val;
            }
            else
            {
                NegativeMoveX_textBox.ForeColor = Color.Red;
            }
        }

        private void NegativeMoveY_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NegativeMoveY_textBox.Text.Replace(',', '.'), out val))
            {
                NegativeMoveY_textBox.ForeColor = Color.Black;
                Setting.General_NegativeY = val;
            }
            else
            {
                NegativeMoveY_textBox.ForeColor = Color.Red;
            }
        }

        private void DowncamMirror_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Downcam_Mirror = DowncamMirror_checkBox.Checked;
            DownCamera.Mirror = Setting.Downcam_Mirror;
        }

        private void UpcamMirror_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Upcam_Mirror = UpcamMirror_checkBox.Checked;
            UpCamera.Mirror = Setting.Upcam_Mirror;
        }
    }	// end of: 	public partial class FormMain : Form



    // ===================================================================================
    // allows addition of color info to displayText 
    public static class RichTextBoxExtensions
    {
        public static void AppendText(this RichTextBox box, string text, Color color)
        {
            try
            {
                if (color != box.ForeColor)
                {
                    box.SelectionStart = box.TextLength;
                    box.SelectionLength = 0;
                    box.SelectionColor = color;
                    box.AppendText(text);
                    box.SelectionColor = box.ForeColor;
                }
                else
                {
                    box.AppendText(text);
                }

            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    // ===================================================================================
    // A message box that is centered on the main form.
    // From: http://www.jasoncarr.com/technology/centering-a-message-box-on-the-active-window-in-csharp

    internal static class CenteredMessageBox
    {
        internal static void PrepToCenterMessageBoxOnForm(Form form)
        {
            MessageBoxCenterHelper helper = new MessageBoxCenterHelper();
            helper.Prep(form);
        }

        private class MessageBoxCenterHelper
        {
            private int messageHook;
            private IntPtr parentFormHandle;

            public void Prep(Form form)
            {
                NativeMethods.CenterMessageCallBackDelegate callBackDelegate = new NativeMethods.CenterMessageCallBackDelegate(CenterMessageCallBack);
                GCHandle.Alloc(callBackDelegate);

                parentFormHandle = form.Handle;
                messageHook = NativeMethods.SetWindowsHookEx(5, callBackDelegate, new IntPtr(NativeMethods.GetWindowLong(parentFormHandle, -6)), NativeMethods.GetCurrentThreadId()).ToInt32();
            }

            private int CenterMessageCallBack(int message, int wParam, int lParam)
            {
                NativeMethods.RECT formRect;
                NativeMethods.RECT messageBoxRect;
                int xPos;
                int yPos;

                if (message == 5)
                {
                    NativeMethods.GetWindowRect(parentFormHandle, out formRect);
                    NativeMethods.GetWindowRect(new IntPtr(wParam), out messageBoxRect);

                    xPos = (int)((formRect.Left + (formRect.Right - formRect.Left) / 2) - ((messageBoxRect.Right - messageBoxRect.Left) / 2));
                    yPos = (int)((formRect.Top + (formRect.Bottom - formRect.Top) / 2) - ((messageBoxRect.Bottom - messageBoxRect.Top) / 2));

                    NativeMethods.SetWindowPos(wParam, 0, xPos, yPos, 0, 0, 0x1 | 0x4 | 0x10);
                    NativeMethods.UnhookWindowsHookEx(messageHook);
                }

                return 0;
            }
        }

        private static class NativeMethods
        {
            internal struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            internal delegate int CenterMessageCallBackDelegate(int message, int wParam, int lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool UnhookWindowsHookEx(int hhk);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [DllImport("kernel32.dll")]
            internal static extern int GetCurrentThreadId();

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr SetWindowsHookEx(int hook, CenterMessageCallBackDelegate callback, IntPtr hMod, int dwThreadId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetWindowPos(int hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        }
    }


    }	// end of: namespace LitePlacer