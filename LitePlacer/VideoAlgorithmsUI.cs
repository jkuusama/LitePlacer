﻿using System;
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
using System.Configuration;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Input;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;

using AForge;
using AForge.Video;
using Newtonsoft.Json;

// To add a function: Add its name to the list of KnownFunctions below, and read more at SetFunctionDefaultParameters() 

namespace LitePlacer
{
    public partial class FormMain : Form
    {
        enum Functions_dataGridViewColumns : int { FunctionColumn, ActiveColumn };
        public List<string> KnownFunctions = new List<string> {"Threshold", "Invert", "Meas. zoom", "Histogram", "Grayscale", "Edge detect",
                "Noise reduction", "Erosion", "Kill color", "Keep color", "Blur", "Gaussian blur", "Hough circles"};
        
        //public List<string> KnownFunctions = new List<string> {"Threshold", "Invert", "Meas. zoom", "Histogram", "Grayscale", "Edge detect",
        //        "Noise reduction", "Erosion", "Kill color", "Keep color", "Blur", "Gaussian blur", "Hough circles", "Filter blobs"};

        public VideoAlgorithmsCollection VideoAlgorithms;

        // =====================================================================================
        // interface to main form:
        Camera cam;

        private void Algorithms_tabPage_Begin()
        {
            DisplayText("Setup Video Processing tab begin");
            VideoProcessingZguard_checkBox.Checked = false;
            SetDownCameraDefaults();
            SetUpCameraDefaults();
            if (DownCam_radioButton.Checked)
            {
                ChangeCamera(DownCamera);
            }
            else
            {
                ChangeCamera(UpCamera);
            }
            JigX_textBox.Text = Setting.General_JigOffsetX.ToString("0.00", CultureInfo.InvariantCulture);
            JigY_textBox.Text = Setting.General_JigOffsetY.ToString("0.00", CultureInfo.InvariantCulture);
            PickupCenterX_textBox.Text = Setting.General_PickupCenterX.ToString("0.00", CultureInfo.InvariantCulture);
            PickupCenterY_textBox.Text = Setting.General_PickupCenterY.ToString("0.00", CultureInfo.InvariantCulture);
            NozzleOffsetX_textBox.Text = Setting.DownCam_NozzleOffsetX.ToString("0.00", CultureInfo.InvariantCulture);
            NozzleOffsetY_textBox.Text = Setting.DownCam_NozzleOffsetY.ToString("0.00", CultureInfo.InvariantCulture);
            Z0toPCB_CamerasTab_label.Text = Setting.General_ZtoPCB.ToString("0.00", CultureInfo.InvariantCulture) + " mm";
            UpcamPositionX_textBox.Text = Setting.UpCam_PositionX.ToString("0.00", CultureInfo.InvariantCulture);
            UpcamPositionY_textBox.Text = Setting.UpCam_PositionY.ToString("0.00", CultureInfo.InvariantCulture);

            ProcessDisplay_checkBox_Checked_Change();
        }

        private void Algorithms_tabPage_End()
        {
        }

        public void InitVideoAlgorithmsUI()
        {
            DownCam_radioButton.Checked = true; // default to Downcamera
            Functions_dataGridView.Rows.Clear();
            DataGridViewComboBoxColumn comboboxColumn =
                 (DataGridViewComboBoxColumn)Functions_dataGridView.Columns[(int)Functions_dataGridViewColumns.FunctionColumn];
            comboboxColumn.Items.Clear();
            comboboxColumn.DataSource = KnownFunctions;

            VideoAlgorithms = new VideoAlgorithmsCollection();
            LoadVideoAlgorithms(VideoAlgorithms); // causes updating of Functions_dataGridView and Function parameters
        }

        // camera change
        private void ChangeCamera(Camera NewCam)
        {
            cam = NewCam;
            SelectCamera(NewCam);
            AlgorithmsTab_RestoreBehaviour();
        }

        private void DownCam_radioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }
            // Both radiobuttons CheckedChanged events fire; we need to react only once
            if (DownCam_radioButton.Checked)
            {
                ChangeCamera(DownCamera);
            }
            else
            {
                ChangeCamera(UpCamera);
            }
        }

        private void UpCam_radioButton_CheckedChanged(object sender, EventArgs e)
        {
            // ChangeCamera(UpCamera);
        }


        private void AlgorithmsTab_RestoreBehaviour()
        {
            if (ProcessDisplay_checkBox.Checked)
            {
                UpdateVideoProcessing();
            }
            else
            {
                StopVideoProcessing();
            }

        }

        // =====================================================================================

        // =====================================================================================
        #region Algorithms Load and Save

        // Load:
        private bool AlgorithmChange = false;   // to prevent parameters etc updates in the middle of process

        private void AlgorithmsLoad_button_Click(object sender, EventArgs e)
        {
            LoadVideoAlgorithms(VideoAlgorithms);
        }

        private void LoadVideoAlgorithms(VideoAlgorithmsCollection Collection)
        {
            AlgorithmChange = true;
            string path = GetPath();
            string FileName = path + VIDEOALGORITHMS_DATAFILE;
            if (File.Exists(FileName))
            {
                DisplayText("LoadVideoAlgorithms from " + FileName);
                List<VideoAlgorithmsCollection.FullAlgorithmDescription> NewList = new List<VideoAlgorithmsCollection.FullAlgorithmDescription>();
                NewList = JsonConvert.DeserializeObject<List<VideoAlgorithmsCollection.FullAlgorithmDescription>>(File.ReadAllText(FileName));
                Collection.AllAlgorithms = NewList;
            }
            else
            {
                LoadOldVideoAlgorithms(path, Collection);
            }
            // fill Algorithm_comboBox
            Algorithm_comboBox.Items.Clear();
            foreach (VideoAlgorithmsCollection.FullAlgorithmDescription Algorithm in Collection.AllAlgorithms)
            {
                Algorithm_comboBox.Items.Add(Algorithm.Name);
            }
            ClearFunctionParameters();
            Algorithm_comboBox.SelectedIndex = 0;
            Functions_dataGridView.CurrentCell = null;
            AlgorithmChange = false;
        }


        private void LoadOldVideoAlgorithms(string path, VideoAlgorithmsCollection Collection)
        {
            // For now (maybe for good?), build an placeholder list
            List<VideoAlgorithmsCollection.FullAlgorithmDescription> NewList = new List<VideoAlgorithmsCollection.FullAlgorithmDescription>();
            NewList.Add(new VideoAlgorithmsCollection.FullAlgorithmDescription { Name = "Homing" });
            NewList.Add(new VideoAlgorithmsCollection.FullAlgorithmDescription { Name = "Fiducials" });
            NewList.Add(new VideoAlgorithmsCollection.FullAlgorithmDescription { Name = "Paper tape" });
            NewList.Add(new VideoAlgorithmsCollection.FullAlgorithmDescription { Name = "Black tape" });
            NewList.Add(new VideoAlgorithmsCollection.FullAlgorithmDescription { Name = "Clear tape" });
            NewList.Add(new VideoAlgorithmsCollection.FullAlgorithmDescription { Name = "Components" });
            NewList.Add(new VideoAlgorithmsCollection.FullAlgorithmDescription { Name = "Nozzle tip" });
            Collection.AllAlgorithms = NewList;
        }

        // ========= Save

        private bool SaveVideoAlgorithms(string FileName, VideoAlgorithmsCollection Collection)
        {
            try
            {
                DisplayText("SaveVideoAlgorithms to " + FileName);
                File.WriteAllText(FileName, JsonConvert.SerializeObject(VideoAlgorithms.AllAlgorithms, Formatting.Indented));
                return true;
            }
            catch (System.Exception excep)
            {
                DisplayText("Saving Video algorithms failed. " + excep.Message);
                return false;
            }
        }

        private void AlgorithmsSave_button_Click(object sender, EventArgs e)
        {
            string path = GetPath();
            SaveVideoAlgorithms(path + VIDEOALGORITHMS_DATAFILE, VideoAlgorithms);
        }

        #endregion Algorithms Load and Save
        // =====================================================================================

        // =====================================================================================
        #region Current video algorithm

        private void Algorithm_comboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            AlgorithmChange = true;
            string AlgorithmName = Algorithm_comboBox.SelectedItem.ToString();
            // DisplayText("Algorithm_comboBox_SelectedIndexChanged(), func: " + AlgorithmName);
            VideoAlgorithms.SelectedAlgorithmChanged(AlgorithmName);
            FillFunctionTable(AlgorithmName);
            FillMeasurementValues(AlgorithmName);
            ClearFunctionParameters();
            Functions_dataGridView.CurrentCell = null;
            AlgorithmChange = false;
            if (ProcessDisplay_checkBox.Checked)
            {
                UpdateVideoProcessing();
            }

        }

        bool ChangeYwithX = false;

        private void FillMeasurementValues(string ListName)
        {
            // User changed the current algorithm. This function fills the measurement value boxes
            MeasurementParametersClass values = VideoAlgorithms.CurrentAlgorithm.MeasurementParameters;
            SearchRound_checkBox.Checked = values.SearchRounds;
            SearchRectangles_checkBox.Checked = values.SearchRectangles;
            SearchComponents_checkBox.Checked = values.SearchComponents;
            // for some reason(??), the order of things to set are not honored. A workaround:
            ChangeYwithX = false;
            Xmin_textBox.Text = values.Xmin.ToString("0.00", CultureInfo.InvariantCulture);
            Xmax_textBox.Text = values.Xmax.ToString("0.00", CultureInfo.InvariantCulture);
            Ymin_textBox.Text = values.Ymin.ToString("0.00", CultureInfo.InvariantCulture);
            Ymax_textBox.Text = values.Ymax.ToString("0.00", CultureInfo.InvariantCulture);
            XmaxDistance_textBox.Text = values.XUniqueDistance.ToString("0.00", CultureInfo.InvariantCulture);
            YmaxDistance_textBox.Text = values.XUniqueDistance.ToString("0.00", CultureInfo.InvariantCulture);
            ChangeYwithX = true;
        }
        // =====================================================================================
        // Add, Remove, Duplicate, Rename

        // ==================
        // helpers
        public string GetName(string StartName, bool rename)
        {
            AlgorithmNameForm GetNameDialog = new AlgorithmNameForm(StartName);
            GetNameDialog.Algorithms = VideoAlgorithms.AllAlgorithms;
            GetNameDialog.Renaming = rename;
            GetNameDialog.StartPosition = FormStartPosition.CenterParent;
            GetNameDialog.ShowDialog(this);
            if (GetNameDialog.OK)
            {
                return GetNameDialog.NewName;
            }
            else
            {
                return null;
            }
        }

        private bool FindLocation(string AlgorithmName, out int loc)
        {
            // returns the index of the named algorithm in AllAlgorithms
            for (int i = 0; i < VideoAlgorithms.AllAlgorithms.Count; i++)
            {
                if (VideoAlgorithms.AllAlgorithms[i].Name == AlgorithmName)
                {
                    loc = i;
                    return true;
                }
            }
            loc = -1;
            return false;
        }

        public static T DeepClone<T>(T obj)
        {
            string clone = JsonConvert.SerializeObject(obj, Formatting.Indented);
            return JsonConvert.DeserializeObject<T>(clone);
        }

        // ==================
        // Button clicks

        private void AddAlgorithm(string NewName)
        {
            DisplayText("Add algorithm " + NewName);
            VideoAlgorithmsCollection.FullAlgorithmDescription Alg = new VideoAlgorithmsCollection.FullAlgorithmDescription();
            Alg.Name = NewName;
            VideoAlgorithms.AllAlgorithms.Add(Alg);
            Algorithm_comboBox.Items.Add(NewName);
            // tapes grid
            for (int i = 0; i < Tapes_dataGridView.RowCount; i++)
            {
                string OrgValue = Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value.ToString();
                // rebuild the selection cell
                DataGridViewComboBoxCell c = new DataGridViewComboBoxCell();
                BuildAlgorithmsCombox(out c);
                Tapes_dataGridView.Rows[i].Cells["Type_Column"] = c;
                Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value = OrgValue;
            }
        }

        private void AddAlgorithm_button_Click(object sender, EventArgs e)
        {
            string NewName = GetName("", false);
            if (NewName == null)
            {
                DisplayText("Add algorithm canceled");
                return;
            }
            AddAlgorithm(NewName);
            Algorithm_comboBox.SelectedIndex = Algorithm_comboBox.Items.Count - 1;
        }

        private void RemoveAlgorithm_button_Click(object sender, EventArgs e)
        {
            int pos = 0;
            string Alg = Algorithm_comboBox.SelectedItem.ToString();
            if (!FindLocation(Alg, out pos))
            {
                DisplayText("Remove algorithm, algorithm not found!");
                return;
            }
            if (pos == 0)
            {
                DisplayText("Homing cannot be removed.");
                return;
            }
            bool AlgInUse = AlgorithmUsed(Alg);
            if (AlgInUse)
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Algorithm " + Alg + " is in use (see log window). Remove anyway?" +
                    "\r\n(Used instances are replaced with Homing)",
                    "OK to remove algorithm?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    return;
                };
            }
            VideoAlgorithms.AllAlgorithms.RemoveAt(pos);
            Algorithm_comboBox.Items.RemoveAt(Algorithm_comboBox.SelectedIndex);
            AlgorithmChange = true;
            ClearFunctionParameters();
            Algorithm_comboBox.SelectedIndex = 0;
            Functions_dataGridView.CurrentCell = null;
            AlgorithmChange = false;
            UpdateVideoProcessing();
            if (AlgInUse)
            {
                RemoveAlgorithmFromUse(Alg);
            }
        }

        private void DuplicateAlgorithm_button_Click(object sender, EventArgs e)
        {
            int loc;
            if (!FindLocation(Algorithm_comboBox.SelectedItem.ToString(), out loc))
            {
                DisplayText("Duplicate algorithm, algorithm not found!");
                return;
            }
            DisplayText("Duplicate algorithm " + VideoAlgorithms.AllAlgorithms[loc].Name);
            VideoAlgorithmsCollection.FullAlgorithmDescription Alg =
                DeepClone<VideoAlgorithmsCollection.FullAlgorithmDescription>(VideoAlgorithms.AllAlgorithms[loc]);
            Alg.Name = VideoAlgorithms.AllAlgorithms[loc].Name + " (duplicate)";
            VideoAlgorithms.AllAlgorithms.Add(Alg);
            Algorithm_comboBox.Items.Add(Alg.Name);
            Algorithm_comboBox.SelectedIndex = Algorithm_comboBox.Items.Count - 1;
            // User propably doesn't want the name+(duplicate), so let's click the rename button automatically
            RenameAlgorithm_button_Click(sender, e);
        }

        private void RenameAlgorithm_button_Click(object sender, EventArgs e)
        {
            if (Algorithm_comboBox.SelectedIndex == 0)
            {
                DisplayText("Homing can't be renamed.");
                return;
            }
            string OldName = Algorithm_comboBox.SelectedItem.ToString();
            string NewName = GetName(OldName, true);
            if (NewName == null)
            {
                DisplayText("Rename algorithm canceled");
                return;
            }
            DisplayText("Rename algorithm to " + NewName);
            int AlgPos;
            if (!FindLocation(OldName, out AlgPos))
            {
                DisplayText("***Rename algorithm, algorithm not found!");
                return;
            }
            VideoAlgorithms.AllAlgorithms[AlgPos].Name = NewName;
            int NamePos = Algorithm_comboBox.SelectedIndex;
            Algorithm_comboBox.Items.RemoveAt(NamePos);
            Algorithm_comboBox.Items.Insert(NamePos, NewName);
            Algorithm_comboBox.SelectedIndex = NamePos;

            // Rename algorithm names if already in use
            // Fiducials on the job table
            foreach (DataGridViewRow row in JobData_GridView.Rows)
            {
                if (row.Cells["GroupMethod"].Value.ToString() == "Fiducials")
                {
                    row.Cells["MethodParamAllComponents"].Value = NewName;
                }
            }
            // Tapes data table
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                string CurrentValue = row.Cells["Type_Column"].Value.ToString();
                row.Cells["Type_Column"].Value = null;
                DataGridViewComboBoxCell c = new DataGridViewComboBoxCell();
                BuildAlgorithmsCombox(out c);
                row.Cells["Type_Column"] = c;
                if (VideoAlgorithms.AlgorithmExists(CurrentValue))
                {
                    row.Cells["Type_Column"].Value = CurrentValue;
                }
                else
                {
                    // if CurrentValue does not exist anymore, the name was changed
                    if (CurrentValue != OldName)
                    {
                        DisplayText("*** Rename algorithm: Assert failed!");  // or something else is off
                    }
                    row.Cells["Type_Column"].Value = NewName;
                }
            }
            // Nozzles

        }

        private void ProcessDisplay_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            ProcessDisplay_checkBox_Checked_Change();
        }

        private void ProcessDisplay_checkBox_Checked_Change()
        {
            if (ProcessDisplay_checkBox.Checked)
            {
                UpdateVideoProcessing();
            }
            else
            {
                StopVideoProcessing();
            }
        }



        // =====================================================================================
        // Check if Algorithm is used somewhere
        private bool AlgorithmUsed(string Alg)
        {
            bool ret = false;
            for (int i = 0; i < JobData_GridView.RowCount; i++)
            {
                if (JobData_GridView.Rows[i].Cells["MethodParamAllComponents"].Value != null)
                {
                    if (JobData_GridView.Rows[i].Cells["MethodParamAllComponents"].Value.ToString() == Alg)
                    {
                        DisplayText("Algorithm \"" + Alg + "\" used in Job data, row " + (i+1).ToString());
                        ret = true;
                    }
                }
            }

            for (int i = 0; i < Tapes_dataGridView.RowCount; i++)
            {
                if (Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value != null)
                {
                    if (Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value.ToString() == Alg)
                    {
                        DisplayText("Algorithm \"" + Alg + "\" used in Tapes data, row " + (i + 1).ToString());
                        ret = true;
                    }
                }
            }

            for (int i = 0; i < NozzlesParameters_dataGridView.RowCount; i++)
            {
                if (NozzlesParameters_dataGridView.Rows[i].Cells["VisionAlgorithm_column"].Value != null)
                {
                    if (NozzlesParameters_dataGridView.Rows[i].Cells["VisionAlgorithm_column"].Value.ToString() == Alg)
                    {
                        DisplayText("Algorithm \"" + Alg + "\" used in Nozzles setup, row " + (i + 1).ToString());
                        ret = true;
                    }

                }
            }
            return ret;
        }

        // =====================================================================================
        // And if algorithm was in use and was deleted, we need to replace it with the (always existing) homing
        private void RemoveAlgorithmFromUse(string Alg)
        {
            // Job data
            for (int i = 0; i < JobData_GridView.RowCount; i++)
            {
                if (JobData_GridView.Rows[i].Cells["MethodParamAllComponents"].Value != null)
                {
                    if (JobData_GridView.Rows[i].Cells["MethodParamAllComponents"].Value.ToString() == Alg)
                    {
                        JobData_GridView.Rows[i].Cells["MethodParamAllComponents"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
                    }
                }
            }

            // Tapes grid
            for (int i = 0; i < Tapes_dataGridView.RowCount; i++)
            {
                string OrgValue = Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value.ToString();
                // rebuild the selection cell
                DataGridViewComboBoxCell c = new DataGridViewComboBoxCell();
                BuildAlgorithmsCombox(out c);
                Tapes_dataGridView.Rows[i].Cells["Type_Column"] = c;
                if (OrgValue == Alg)
                {
                    Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
                }
                else
                {
                    Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value = OrgValue;
                }
            }

            // Nozzles
            for (int i = 0; i < NozzlesParameters_dataGridView.RowCount; i++)
            {
                if (NozzlesParameters_dataGridView.Rows[i].Cells["VisionAlgorithm_column"].Value != null)
                {
                    if (NozzlesParameters_dataGridView.Rows[i].Cells["VisionAlgorithm_column"].Value.ToString() == Alg)
                    {
                        NozzlesParameters_dataGridView.Rows[i].Cells["VisionAlgorithm_column"].Value = VideoAlgorithms.AllAlgorithms[0].Name;
                    }

                }
            }
        }



        #endregion Current video algorithm
        // =====================================================================================

        // =====================================================================================
        #region Functions and parameters

        // =====================================================================================
        // Buttons:

        private void AddFunction_button_Click(object sender, EventArgs e)
        {
            // adding a row will change current cell; we don't want false update of function parameters
            AlgorithmChange = true;
            DataGridViewSelectedRowCollection SelectedRows = Functions_dataGridView.SelectedRows;
            int index = 0;
            if (Functions_dataGridView.Rows.Count == 0)
            {
                // grid is empty:
                Functions_dataGridView.Rows.Insert(0);
            }
            else
            {
                // insert at end
                Functions_dataGridView.Rows.Insert(Functions_dataGridView.Rows.Count);
                index = Functions_dataGridView.Rows.Count - 1;
            };
            Functions_dataGridView.CurrentCell = null;
            AlgorithmChange = false;
        }

        // ===================
        private void RemoveFunction_button_Click(object sender, EventArgs e)
        {
            // If function exists, remove it.
            if (VideoAlgorithms.CurrentFunctionIndex >= 0)
            {
                bool WasActive =
                    VideoAlgorithms.CurrentAlgorithm.FunctionList[VideoAlgorithms.CurrentFunctionIndex].Active;
                VideoAlgorithms.CurrentAlgorithm.FunctionList.RemoveAt(VideoAlgorithms.CurrentFunctionIndex);
                VideoAlgorithms.CurrentFunctionIndex = -1;
                // We don't want false update of function parameters
                AlgorithmChange = true;
                FillFunctionTable(VideoAlgorithms.CurrentAlgorithm.Name);
                ClearFunctionParameters();
                Functions_dataGridView.CurrentCell = null;
                AlgorithmChange = false;
                if (WasActive)
                {
                    UpdateVideoProcessing();
                }
            }
        }

        // ===================
        private void MoveUp_button_Click(object sender, EventArgs e)
        {
            if (Functions_dataGridView.CurrentCell == null)
            {
                DisplayText("Move up, cell=null");
                return;
            }
            int OldPos = Functions_dataGridView.CurrentCell.RowIndex;
            if (OldPos == 0)
            {
                DisplayText("Move up, at top (row==0)");
                return;
            }
            MoveFunction(OldPos, OldPos - 1);
        }

        // ===================
        private void MoveDown_button_Click(object sender, EventArgs e)
        {
            if (Functions_dataGridView.CurrentCell == null)
            {
                DisplayText("Move down, cell=null");
                return;
            }
            int OldPos = Functions_dataGridView.CurrentCell.RowIndex;
            if (OldPos >= Functions_dataGridView.RowCount - 1)
            {
                DisplayText("Move down, at bottom)");
                return;
            }
            MoveFunction(OldPos, OldPos + 1);
        }

        // ===================
        private void MoveFunction(int OldPos, int NewPos)
        {
            // Re-arranges functions in the UI
            AForgeFunctionDefinition funct = VideoAlgorithms.CurrentAlgorithm.FunctionList[OldPos];
            VideoAlgorithms.CurrentAlgorithm.FunctionList.RemoveAt(OldPos);
            if(VideoAlgorithms.CurrentAlgorithm.FunctionList.Count <= NewPos)
                VideoAlgorithms.CurrentAlgorithm.FunctionList.Add(funct);
            else
                VideoAlgorithms.CurrentAlgorithm.FunctionList.Insert(NewPos, funct);
            int col = Functions_dataGridView.CurrentCell.ColumnIndex;
            AlgorithmChange = true;
            FillFunctionTable(VideoAlgorithms.CurrentAlgorithm.Name);
            ClearFunctionParameters();
            Functions_dataGridView.CurrentCell = null; // to force the change event at last statement
            AlgorithmChange = false;
            Functions_dataGridView.CurrentCell = Functions_dataGridView[col, NewPos];
            if (funct.Active)
            {
                UpdateVideoProcessing();
            }
        }

        // ===================
        private void Algorithm_Measure_button_Click(object sender, EventArgs e)
        {
            Camera cam = UpCamera;
            if (DownCam_radioButton.Checked)
            {
                cam = DownCamera;
            }
            cam.BuildMeasurementFunctionsList(VideoAlgorithms.CurrentAlgorithm.FunctionList);
            cam.MeasurementParameters = VideoAlgorithms.CurrentAlgorithm.MeasurementParameters;
            cam.Measure(out double X, out double Y, out int err, true);
        }


        // =====================================================================================
        // Functions_dataGridView 

        // =====================================================================================
        // Helper functions:

        public void BuildAlgorithmsCombox(out DataGridViewComboBoxCell Cout)
        {
            DataGridViewComboBoxCell c = new DataGridViewComboBoxCell();
            for (int i = 0; i < VideoAlgorithms.AllAlgorithms.Count; i++)
            {
                c.Items.Add(VideoAlgorithms.AllAlgorithms[i].Name);
            }
            Cout = c;
        }


        void FillFunctionTable(string AlgorithmName)
        {
            // User changed the current algorithm or deleted a fuction. 
            // This function (re-)fills Algorithms_dataGridView function column
            Functions_dataGridView.Rows.Clear();
            int row = 0;
            AForgeFunctionDefinition func = new AForgeFunctionDefinition();

            for (int i = 0; i < VideoAlgorithms.CurrentAlgorithm.FunctionList.Count; i++)
            {
                func = VideoAlgorithms.CurrentAlgorithm.FunctionList[i];
                if (KnownFunctions.Contains(func.Name))
                {
                    int index = KnownFunctions.IndexOf(func.Name);
                    Functions_dataGridView.Rows.Add();
                    Functions_dataGridView.Rows[row].Cells[(int)Functions_dataGridViewColumns.FunctionColumn].Value =
                        func.Name;
                    Functions_dataGridView.Rows[row].Cells[(int)Functions_dataGridViewColumns.ActiveColumn].Value =
                        func.Active;
                    row++;
                }
            }
            Update_GridView(Functions_dataGridView);
        }

        // =====================================================================================
        // Grid cell events:

        private void Functions_dataGridView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (AlgorithmChange)
            {
                //DisplayText("Functions_dataGridView_CellValueChanged(), AlgorithmChange");
                return;
            }
            if (Functions_dataGridView.CurrentCell == null)
            {
                //DisplayText("Functions_dataGridView_CellValueChanged(), cell=null");
                return;
            }
            int row = Functions_dataGridView.CurrentCell.RowIndex;
            int col = Functions_dataGridView.CurrentCell.ColumnIndex;
            //DisplayText("Functions_dataGridView_CellValueChanged(), " + row.ToString() + ", " + col.ToString());
            int FunctCol = (int)Functions_dataGridViewColumns.FunctionColumn;
            int ActiveCol = (int)Functions_dataGridViewColumns.ActiveColumn;

            if (Functions_dataGridView.Rows[row].Cells[FunctCol].Value == null)
            {
                //DisplayText("value: null");
                return;
            };
            //DisplayText("value: " + Functions_dataGridView.Rows[row].Cells[FunctCol].Value.ToString());
            Update_GridView(Functions_dataGridView);

            if (col == FunctCol)
            {
                // Function column changed
                string FunctionName = Functions_dataGridView.Rows[row].Cells[FunctCol].Value.ToString();
                VideoAlgorithms.CurrentFunctionIndex = row;
                VideoAlgorithms.CurrentAlgorithm.FunctionList[row].Name = FunctionName;
                SetFunctionDefaultParameters(FunctionName);
                VideoAlgorithms.CurrentAlgorithm.FunctionList[row].Active = false;  // newly selected function is inactive by default
                UpdateParameterTargets(FunctionName);
                // No need to update video processing
            }
            else
            {
                // active column changed
                VideoAlgorithms.CurrentAlgorithm.FunctionList[row].Active =
                    (bool)Functions_dataGridView.Rows[row].Cells[ActiveCol].Value;
                UpdateVideoProcessing();
            }
        }

        private void Functions_dataGridView_CurrentCellChanged(object sender, EventArgs e)
        {
            if (AlgorithmChange)
            {
                DisplayText("Functions_dataGridView_CurrentCellChanged(), AlgorithmChange");
                return;
            }
            if (Functions_dataGridView.CurrentCell == null)
            {
                DisplayText("Functions_dataGridView_CurrentCellChanged(), cell=null");
                return;
            }
            int row = Functions_dataGridView.CurrentCell.RowIndex;
            int col = Functions_dataGridView.CurrentCell.ColumnIndex;
            DisplayText("Functions_dataGridView_CurrentCellChanged(), " + row.ToString() + ", " + col.ToString());
            VideoAlgorithms.CurrentFunctionIndex = row;
            if (Functions_dataGridView.Rows[row].Cells[(int)Functions_dataGridViewColumns.FunctionColumn].Value == null)
            {
                // user is creating new function and has not yet selected the value
                return;
            }
            UpdateParameterTargets(Functions_dataGridView.Rows[row].Cells[(int)Functions_dataGridViewColumns.FunctionColumn].Value.ToString());
        }

        private void Functions_dataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (Functions_dataGridView.CurrentCell == null)
            {
                DisplayText("Functions_dataGridView_CurrentCellDirtyStateChanged(), cell=null");
                return;
            }

            int row = Functions_dataGridView.CurrentCell.RowIndex;
            int col = Functions_dataGridView.CurrentCell.ColumnIndex;

            DisplayText("Functions_dataGridView_CurrentCellDirtyStateChanged: "
                + row.ToString() + ", " + col.ToString());
            // Return if not dirty; otherwise the stuff is executed twice (once when it changes, once when it becomes clean)
            if (!Functions_dataGridView.IsCurrentCellDirty)
            {
                return;
            }
            Update_GridView(Functions_dataGridView);
        }


        // =====================================================================================
        // Functions parameters 
        // =====================================================================================

        // To add a function to UI: Add its name and default parameters to the case clause in function SetFunctionDefaultParameters().
        // Enable the required parameters in UpdateParameterTargets)( and write the explanatiopn text there.
        // Add the function to Camera.cs, section "Functions compatible with lists:"


        private void SetFunctionDefaultParameters(string FunctionName)
        {
            // The function is just created. Function is 
            AForgeFunctionDefinition funct = VideoAlgorithms.CurrentAlgorithm.FunctionList[VideoAlgorithms.CurrentFunctionIndex];
            switch (FunctionName)
            {
                // switch by the selected algorithm:  
                case "Filter blobs":
                    funct.parameterDouble = 10.0;
                    break;

                case "Blur":
                    break;		// no parameters

                case "Histogram":
                    break;		// no parameters

                case "Grayscale":
                    break;		// no parameters

                case "Invert":
                    break;      // no parameters

                case "Erosion":
                    break;      // no parameters

                case "Edge detect":
                    // Type, maximum difference of processing pixel with neighboring pixels in 8 direction
                    funct.parameterInt = 3;
                    break;

                case "Kill color":
                    // radius and color
                    funct.parameterInt = 10;
                    funct.R = 128;
                    funct.G = 128;
                    funct.B = 128;
                    break;

                case "Keep color":
                    // radius and color
                    funct.parameterInt = 10;
                    funct.R = 128;
                    funct.G = 128;
                    funct.B = 128;
                    break;

                case "Meas. zoom":
                    funct.parameterDouble = 2.0;
                    break;

                case "Gaussian blur":
                    funct.parameterDouble = 2.0;
                    break;

                case "Threshold":
                    funct.parameterInt = 128;
                    break;

                case "Hough circles":
                    funct.parameterInt = 128;
                    break;

                default:
                    break;
            }
            return;
        }

        private void UpdateParameterTargets(string Name)
        {
            // This function is called when user changes a function in Algorithms_dataGridView
            // The function updates the parameter editing boxes
            // make all inactive
            ClearFunctionParameters();
            // activate those that this function needs, with description text, 
            // and put in default values
            switch (Name)
            {
                // switch by the selected algorithm:  
                case "Blur":
                    // no parameters
                    FunctionExplanation_textBox.Text = "Blurs the image, reducing the effects of camera noise " +
                        "and possible imperfections in the target outline";
                    FunctionExplanation_textBox.Visible = true;
                    break;

                case "Histogram":
                    FunctionExplanation_textBox.Text = "Increases contrast in the image";
                    FunctionExplanation_textBox.Visible = true;
                    break;		// no parameters

                case "Grayscale":
                    FunctionExplanation_textBox.Text = "Converts the image to grayscale";
                    FunctionExplanation_textBox.Visible = true;
                    break;		// no parameters

                case "Invert":
                    FunctionExplanation_textBox.Text = "Inverts the image; the detection functions are looking for" +
                        " white image on black background.";
                    FunctionExplanation_textBox.Visible = true;
                    break;		// no parameters

                case "Erosion":
                    FunctionExplanation_textBox.Text = "Assigns minimum value of surrounding pixels to each pixel of the result image." +
                        "  Removes noisy pixels, shrinks objects.";
                    FunctionExplanation_textBox.Visible = true;
                    break;		// no parameters

                case "Edge detect":
                    // single int parameter, 1..4
                    EnableInt(1, 4, "Operator type:");
                    FunctionExplanation_textBox.Text = "Finds edges in the image:\r\n" +
                        "1: Using Sobel operator.\r\n" +
                        "2: Calculating maximum difference between pixels in 4 directions around the processing pixel.\r\n" +
                        "3: Calculating maximum difference of processing pixel with neighboring pixels in 8 direction.\r\n" +
                        "4: Applying Canny edge detector";
                    FunctionExplanation_textBox.Visible = true;
                    break;

                case "Kill color":
                    // int and RGB parameter
                    EnableInt(0, 255, "Radius:");
                    EnableRGB("Color to remove");
                    FunctionExplanation_textBox.Text = "Removes color that is inside of RGB sphere " +
                        "with specified center color and radius.";
                    FunctionExplanation_textBox.Visible = true;
                    break;

                case "Keep color":
                    // int and RGB parameter
                    EnableInt(0, 255, "Radius:");
                    EnableRGB("Color to keep:");
                    FunctionExplanation_textBox.Text = "Removes color that is outside of RGB sphere " +
                        "with specified center color and radius.";
                    FunctionExplanation_textBox.Visible = true;
                    break;

                case "Meas. zoom":
                    // one double parameter
                    EnableDouble("Zoom factor:");
                    FunctionExplanation_textBox.Text = "Enlargens the image that is used for measurements.";
                    FunctionExplanation_textBox.Visible = true;

                    break;

                case "Gaussian blur":
                    // one double parameter
                    EnableDouble("Sigma:");
                    FunctionExplanation_textBox.Text = "Another method to blur the image: gaussian blur with kernel size of 11.";
                    FunctionExplanation_textBox.Visible = true;
                    break;

                case "Hough circles":
                    // int and double parameter
                    EnableInt(0, 255, "Diameter:");
                    EnableDouble("Intensity:");
                    FunctionExplanation_textBox.Text = "Finds partial circles with specified diameter.\r\n"
                        + "Result intensity correlates to match quality.";
                    FunctionExplanation_textBox.Visible = true;
                    break;

                case "Threshold":
                    // one int parameter
                    EnableInt(0, 255, "Threshold:");
                    FunctionExplanation_textBox.Text = "Makes the image black and white.";
                    FunctionExplanation_textBox.Visible = true;
                    break;

                default:
                    break;
            }
            return;

            // local functions for UpdateParameterTargets():
            void EnableInt(int min, int max, string label)
            {
                IntParameter_numericUpDown.Minimum = min;
                IntParameter_numericUpDown.Maximum = max;
                IntParameter_numericUpDown.Value =
                    VideoAlgorithms.CurrentAlgorithm.FunctionList[VideoAlgorithms.CurrentFunctionIndex].parameterInt;
                IntParameter_numericUpDown.Visible = true;
                IntParameter_label.Text = label;
                IntParameter_label.Visible = true;
            }

            void EnableDouble(string label)
            {
                DoubleParameter_textBox.Text =
                    VideoAlgorithms.CurrentAlgorithm.FunctionList[VideoAlgorithms.CurrentFunctionIndex].parameterDouble.ToString();
                DoubleParameter_textBox.Visible = true;
                DoubleParameter_label.Text = label;
                DoubleParameter_label.Visible = true;
            }

            void EnableRGB(string label)
            {
                R_label.Visible = true;
                G_label.Visible = true;
                B_label.Visible = true;
                R_numericUpDown.Visible = true;
                G_numericUpDown.Visible = true;
                B_numericUpDown.Visible = true;
                R_numericUpDown.Value =
                    VideoAlgorithms.CurrentAlgorithm.FunctionList[VideoAlgorithms.CurrentFunctionIndex].R;
                G_numericUpDown.Value =
                    VideoAlgorithms.CurrentAlgorithm.FunctionList[VideoAlgorithms.CurrentFunctionIndex].G;
                B_numericUpDown.Value =
                    VideoAlgorithms.CurrentAlgorithm.FunctionList[VideoAlgorithms.CurrentFunctionIndex].B;
                RGBParameter_label.Text = label;
                RGBParameter_label.Visible = true;
            }
        }       // UpdateParameterTargets() end



        private void ClearFunctionParameters()
        {
            IntParameter_label.Text = "--";
            IntParameter_label.Visible = false;
            IntParameter_numericUpDown.Visible = false;

            DoubleParameter_label.Text = "--";
            DoubleParameter_label.Visible = false;
            DoubleParameter_textBox.Text = "";
            DoubleParameter_textBox.Visible = false;

            RGBParameter_label.Text = "--";
            RGBParameter_label.Visible = false;
            R_numericUpDown.Visible = false;
            G_numericUpDown.Visible = false;
            B_numericUpDown.Visible = false;
            R_label.Visible = false;
            G_label.Visible = false;
            B_label.Visible = false;

            FunctionExplanation_textBox.Text = "";
            FunctionExplanation_textBox.Visible = false;
        }

        private void UpdateVideoProcessing()
        {
            // User changed something, that (potentially) affects the current video processing
            // Note, that there might not be a current algorithm
            if (!ProcessDisplay_checkBox.Checked)
            {
                return;
            }
            if (VideoAlgorithms.CurrentAlgorithm == null)
            {
                DisplayText("UpdateVideoProcessing(), no current algorithm");
                return;
            }
            // Pass CurrentAlgorithm to camera
            DisplayText("UpdateVideoProcessing()");
            Camera cam = new Camera(this);
            if (DownCam_radioButton.Checked)
            {
                DownCamera.BuildDisplayFunctionsList(VideoAlgorithms.CurrentAlgorithm.FunctionList);
            }
            else
            {
                UpCamera.BuildDisplayFunctionsList(VideoAlgorithms.CurrentAlgorithm.FunctionList);
            }
            UpdateSearchFunctions();
        }

        private void StopVideoProcessing()
        {
            DisplayText("StopVideoProcessing()");
            if (DownCam_radioButton.Checked)
            {
                DownCamera.ClearDisplayFunctionsList();
            }
            if (UpCam_radioButton.Checked)
            {
                UpCamera.ClearDisplayFunctionsList();
            }
        }


        // =====================================================================================
        // Functions parameter changes:
        private void IntParameter_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            VideoAlgorithms.CurrentFunction_NewInt((int)IntParameter_numericUpDown.Value);
            UpdateVideoProcessing();
        }

        private void DoubleParameter_textBox_TextChanged(object sender, EventArgs e)
        {
            double val = 0.0;
            CommasToPoints(DoubleParameter_textBox);
            if (double.TryParse(DoubleParameter_textBox.Text, out val))
            {
                Ymin_textBox.ForeColor = Color.Black;
                VideoAlgorithms.CurrentFunction_NewDouble(val);
                UpdateVideoProcessing();
            }
            else
            {
                Ymin_textBox.ForeColor = Color.Red;
            }
        }

        private void R_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            VideoAlgorithms.CurrentFunction_NewR((int)R_numericUpDown.Value);
            UpdateVideoProcessing();
        }

        private void G_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            VideoAlgorithms.CurrentFunction_NewG((int)G_numericUpDown.Value);
            UpdateVideoProcessing();
        }

        private void B_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            VideoAlgorithms.CurrentFunction_NewB((int)B_numericUpDown.Value);
            UpdateVideoProcessing();
        }

        private void Functions_dataGridView_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            DisplayText("Functions_dataGridView_DataError ");
        }
        #endregion Functions and parameters
        // =====================================================================================

        // =====================================================================================
        #region search, size and distance

        private void UpdateSearchFunctions()
        {
            Camera cam = new Camera(this);
            if (DownCam_radioButton.Checked)
            {
                cam = DownCamera;
            }
            else
            {
                cam = UpCamera;
            }
            cam.FindCircles = VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.SearchRounds;
            cam.FindRectangles = VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.SearchRectangles;
            cam.FindComponent = VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.SearchComponents;
        }


        // search boxes
        private void SearchRound_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.SearchRounds = SearchRound_checkBox.Checked;
            UpdateSearchFunctions();
        }

        private void SearchRectangles_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.SearchRectangles = SearchRectangles_checkBox.Checked;
            UpdateSearchFunctions();
        }

        private void SearchComponents_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.SearchComponents = SearchComponents_checkBox.Checked;
            UpdateSearchFunctions();
        }

        private void ShowPixels_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (ShowPixels_checkBox.Checked) 
            {
                Cam_pictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
            }
            else
            {
                Cam_pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            }
            Setting.Cam_ShowPixels = ShowPixels_checkBox.Checked;
        }

        // =====================================================================================
        // Textbox values and interaction
        void CommasToPoints(TextBox box)
        {
            int pos = box.SelectionStart;
            box.Text = box.Text.Replace(',', '.');
            box.SelectionStart = pos;
        }



        private void Xmin_textBox_TextChanged(object sender, EventArgs e)
        {
            double val = 0.0;
            CommasToPoints(Xmin_textBox);
            if (double.TryParse(Xmin_textBox.Text, out val))
            {
                if (val >= 0.0)
                {
                    if (ChangeYwithX)
                    {
                        Ymin_textBox.Text = Xmin_textBox.Text;
                    }
                    Xmin_textBox.ForeColor = Color.Black;
                    VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.Xmin = val;
                    return;
                }
            }
            Xmin_textBox.ForeColor = Color.Red;
        }



        private void Xmax_textBox_TextChanged(object sender, EventArgs e)
        {
            double val = 0.0;
            CommasToPoints(Xmax_textBox);
            if (double.TryParse(Xmax_textBox.Text, out val))
            {
                if (val >= 0.0)
                {
                    if (ChangeYwithX)
                    {
                        Ymax_textBox.Text = Xmax_textBox.Text;
                    }
                    Xmax_textBox.ForeColor = Color.Black;
                    VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.Xmax = val;
                    return;
                }
            }
            Xmax_textBox.ForeColor = Color.Red;
        }

        private void Ymin_textBox_TextChanged(object sender, EventArgs e)
        {
            double val = 0.0;
            CommasToPoints(Ymin_textBox);
            if (double.TryParse(Ymin_textBox.Text, out val))
            {
                if (val >= 0.0)
                {
                    Ymin_textBox.ForeColor = Color.Black;
                    VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.Ymin = val;
                    return;
                }
            }
            Ymin_textBox.ForeColor = Color.Red;
        }

        private void Ymax_textBox_TextChanged(object sender, EventArgs e)
        {
            double val = 0.0;
            CommasToPoints(Ymax_textBox);
            if (double.TryParse(Ymax_textBox.Text, out val))
            {
                if (val >= 0.0)
                {
                    Ymax_textBox.ForeColor = Color.Black;
                    VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.Ymax = val;
                    return;
                }
            }
            Ymax_textBox.ForeColor = Color.Red;
        }


        private void XmaxDistance_textBox_TextChanged(object sender, EventArgs e)
        {
            double val = 0.0;
            CommasToPoints(XmaxDistance_textBox);
            if (double.TryParse(XmaxDistance_textBox.Text, out val))
            {
                if (val >= 0.0)
                {
                    if (ChangeYwithX)
                    {
                        YmaxDistance_textBox.Text = XmaxDistance_textBox.Text;
                    }
                    XmaxDistance_textBox.ForeColor = Color.Black;
                    VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.XUniqueDistance = val;
                    return;
                }
            }
            XmaxDistance_textBox.ForeColor = Color.Red;
        }



        private void YmaxDistance_textBox_TextChanged(object sender, EventArgs e)
        {
            double val = 0.0;
            CommasToPoints(YmaxDistance_textBox);
            if (double.TryParse(YmaxDistance_textBox.Text, out val))
            {
                if (val >= 0.0)
                {
                    YmaxDistance_textBox.ForeColor = Color.Black;
                    VideoAlgorithms.CurrentAlgorithm.MeasurementParameters.YUniqueDistance = val;
                    return;
                }
            }
            YmaxDistance_textBox.ForeColor = Color.Red;
        }
        #endregion search, size and distance
        // =====================================================================================
    }

}
