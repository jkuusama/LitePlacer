using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using System.Windows.Forms;
using System.Threading;
using System.Globalization;

namespace LitePlacer
{
    class NozzleCalibrationClass
    {
        public class NozzlePoint
        {
            public double Angle;
            public double X;  // X offset from nominal, in mm's, at angle
            public double Y;  // same for Y
        }

        public class CalibrationPointsList: List<NozzlePoint> { }  // calibration data for one nozzle

        public class NozzleData
        {
            public CalibrationPointsList CalibrationPoints;
            public bool Calibrated;
        }

        public List<NozzleData> NozzleDataAllNozzles= new List<NozzleData>();      // calibration data for all nozzles


#pragma warning disable CA2235 // Mark all non-serializable fields
        private Camera Cam;
        private CNC Cnc;
        private static FormMain MainForm;
#pragma warning restore CA2235 // Mark all non-serializable fields



        public NozzleCalibrationClass(Camera MyCam, CNC MyCnc, FormMain MainF)
        {
            MainForm = MainF;
            Cam = MyCam;
            Cnc = MyCnc;
        }



        // =================================================================================
        // save and load from disk
        // =================================================================================
        // At end of load, we update "calibrated" column in the datagrid

        public void UpdateNozzleGridView()
        {
            if (MainForm.NozzlesParameters_dataGridView.Rows.Count != MainForm.Setting.Nozzles_count)
            {
                MainForm.UpdateNozzlesGridSize();
            }
            for (int i = 0; i < MainForm.NozzlesParameters_dataGridView.Rows.Count; i++)
            {
                if (i<NozzleDataAllNozzles.Count)
                {
                    MainForm.NozzlesParameters_dataGridView.Rows[i].Cells["NozzleCalibrated_Column"].Value =
                        NozzleDataAllNozzles[i].Calibrated;
                }
            }
            MainForm.Update_GridView(MainForm.NozzlesParameters_dataGridView);
        }


        public bool SaveNozzlesCalibration(string FileName)
        {
            try
            {
                MainForm.DisplayText("Saving nozzle calibration data to " + FileName);
                File.WriteAllText(FileName, JsonConvert.SerializeObject(NozzleDataAllNozzles, Formatting.Indented));
                return true;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (System.Exception excep)
            {
                MainForm.DisplayText("Saving nozzle calibration failed. " + excep.Message);
                return false;
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        public void AdjustNozzleCalibrationDataCount()
        {
            while (NozzleDataAllNozzles.Count < MainForm.Setting.Nozzles_count)
            {
                NozzleData Nozzle = new NozzleData();
                Nozzle.CalibrationPoints = new CalibrationPointsList();
                Nozzle.Calibrated = false;
                NozzleDataAllNozzles.Add(Nozzle);
            }

            while ((NozzleDataAllNozzles.Count > MainForm.Setting.Nozzles_count) && (MainForm.Setting.Nozzles_count > 0))
            {
                NozzleDataAllNozzles.RemoveAt(NozzleDataAllNozzles.Count - 1);
            }
        }


        public bool LoadNozzlesCalibration(string FileName)
        {
            try
            {
                bool StartStatusSave = MainForm.StartingUp; 
                if (File.Exists(FileName))
                {
                    MainForm.DisplayText("Loading nozzle calibration data from " + FileName);
                    List<NozzleData> NewData = JsonConvert.DeserializeObject<List<NozzleData>>(File.ReadAllText(FileName));
                    NozzleDataAllNozzles = NewData;
                    return true;
                }
                else
                {
                    MainForm.StartingUp = false;
                    MainForm.ShowMessageBox(
                        "Nozzle calibration data not found.\n\r" +
                        "(If you just started or upgraded, this is expected.)",
                        "Nozzle calibration data not found", MessageBoxButtons.OK);
                    MainForm.StartingUp = StartStatusSave;
                    for (int i = 0; i < MainForm.Setting.Nozzles_count; i++)
                    {
                        NozzleData Nozzle = new NozzleData();
                        Nozzle.CalibrationPoints = new CalibrationPointsList();
                        Nozzle.Calibrated = false;
                        NozzleDataAllNozzles.Add(Nozzle);
                    }
                    UpdateNozzleGridView();
                    return true;
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (System.Exception excep)
            {
                MainForm.DisplayText("Loading nozzles calibration data failed. " + excep.Message);
                NozzleDataAllNozzles.Clear();
                for (int i = 0; i < MainForm.Setting.Nozzles_count; i++)
                {
                    NozzleData Nozzle = new NozzleData();
                    Nozzle.CalibrationPoints = new CalibrationPointsList();
                    Nozzle.Calibrated = false;
                    NozzleDataAllNozzles.Add(Nozzle);
                }
                UpdateNozzleGridView();
                return false;
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }


        // =================================================================================
        // Calibration
        // =================================================================================


        // =================================================================================
        // Use calibration
        public bool GetPositionCorrection_m(double angle, out double X, out double Y)
        {
            X = 0.0;
            Y = 0.0;
            if (MainForm.Setting.Placement_OmitNozzleCalibration)
            {
                return true;
            };

            if ( (MainForm.Setting.Nozzles_current > MainForm.Setting.Nozzles_count) ||
                (MainForm.Setting.Nozzles_current <= 0))
            {
                MainForm.ShowMessageBox(
                                   "Current nozzle number is invalid.",
                                   "Nozzle number problem", MessageBoxButtons.OK);
                return false;
            }

            if (MainForm.Setting.Nozzles_current == 0)
            {
                MainForm.ShowMessageBox(
                                   "No nozzle in the adapter. (If there is, fix thestatus on Seup Nozzles page.)",
                                   "Nozzle number problem", MessageBoxButtons.OK);
                return false;
            }


            if (!NozzleDataAllNozzles[MainForm.Setting.Nozzles_current-1].Calibrated)
            {
                DialogResult dialogResult = MainForm.ShowMessageBox(
                    "Nozzle not calibrated. Calibrate now?",
                    "Nozzle not calibrated", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    X = 0.0;
                    Y = 0.0;
                    return false;
                };
                // Do calibration: Save current position, ..
                double CurrX = Cnc.CurrentX;
                double CurrY = Cnc.CurrentY;
                double CurrA = Cnc.CurrentA;
                // calibrate, ..
                if (!MainForm.CalibrateNozzle_m())
                {
                    X = 0;
                    Y = 0;
                    return false;
                }
                // and return to the previous position
                if (!MainForm.CNC_XYA_m(CurrX, CurrY, CurrA))
                {
                    X = 0;
                    Y = 0;
                    return false;
                }
            };

            // move angle to 0..360
            while (angle < 0)
            {
                angle = angle + 360.0;
            };
            while (angle > 360.0)
            {
                angle = angle - 360.0;
            }
            // If angle is 360, return the value for 0
            if (angle > 359.98)
            {
                X = NozzleDataAllNozzles[MainForm.Setting.Nozzles_current - 1].CalibrationPoints[0].X;
                Y = -NozzleDataAllNozzles[MainForm.Setting.Nozzles_current - 1].CalibrationPoints[0].Y;
                return true;
            };
            List<NozzlePoint> Points = NozzleDataAllNozzles[MainForm.Setting.Nozzles_current - 1].CalibrationPoints;
            for (int i = 0; i < Points.Count; i++)
            {
                if (Math.Abs(angle - Points[i].Angle) < 1.0)
                {
                    // we had a calibration value for exact angle asked
                    X = Points[i].X;
                    Y = -Points[i].Y;
                    return true;
                }
                if ((angle > Points[i].Angle)
                    &&
                    (angle < Points[i + 1].Angle)
                    &&
                    (Math.Abs(angle - Points[i + 1].Angle) > 1.0))
                {
                    // we are between points, and next point is not what was asked either: 
                    // linear interpolation:
                    double fract = (angle - Points[i + 1].Angle) / (Points[i + 1].Angle - Points[i].Angle);
                    X = Points[i].X + fract * (Points[i + 1].X - Points[i].X);
                    Y = Points[i].Y + fract * (Points[i + 1].Y - Points[i].Y);
                    Y = -Y;
                    return true;
                }
            }
            MainForm.ShowMessageBox(
                "Nozzle Calibration value read: value not found",
                "Sloppy programmer error",
                MessageBoxButtons.OK);
            X = 0;
            Y = 0;
            return false;
        }


        // =================================================================================
        // Do calibration for one nozzle. Nozzle is already loaded and lowered to the up camera location. Camera measurement is set up.
        public bool Calibrate()
        {
            if (!Cam.IsRunning())
            {
                MainForm.DisplayText("Attempt to calibrate Nozzle, camera is not running!", System.Drawing.KnownColor.DarkRed, true);
                return false;
            }
            if (NozzleDataAllNozzles.Count < MainForm.Setting.Nozzles_current - 1)
            {
                MainForm.DisplayText("Attempt to calibrate Nozzle, current nozzle > amount of data!", System.Drawing.KnownColor.DarkRed, true);
                return false;
            }

            NozzleData Nozzle = new NozzleData();
            Nozzle.CalibrationPoints = new CalibrationPointsList();
            Nozzle.Calibrated = false;
            // I goes in .1 of degrees. Makes sense to have the increase so, that multiplies of 45 are hit
            for (int i = 0; i <= 3600; i = i + 225)
            {
                NozzlePoint Point = new NozzlePoint();
                Point.Angle = Convert.ToDouble(i) / 10.0;
                if (!CNC_A_m(Point.Angle))
                {
                    NozzleDataAllNozzles[MainForm.Setting.Nozzles_current - 1] = Nozzle;
                    return false;
                }
                for (int tries = 0; tries < 10; tries++)
                {
                    if (Cam.Measure(out Point.X, out Point.Y, out double Ares, true))
                    {
                        break;
                    }
                    if (tries >= 9)
                    {
                        MainForm.ShowMessageBox(
                            "Nozzle calibration: Can't see Nozzle",
                            "No Circle found",
                            MessageBoxButtons.OK);
                        NozzleDataAllNozzles[MainForm.Setting.Nozzles_current - 1] = Nozzle;
                        return false;
                    }
                }
                MainForm.DisplayText("A: " + Point.Angle.ToString("0.000") + ", X: " + Point.X.ToString("0.000") + ", Y: " + Point.Y.ToString("0.000"));
                Nozzle.CalibrationPoints.Add(Point);
            }
            Nozzle.Calibrated = true;
            NozzleDataAllNozzles[MainForm.Setting.Nozzles_current - 1] = Nozzle;
            return true;
        }




        // =================================================================================
        // CNC interface functions
        // =================================================================================

        // =================================================================================
        // Nozzle.Move(): Takes machine to position, where nozzle, whewn lowered down, eds up in the coordinates specified
        // Correction is applied, if enabled.

        public bool Move_m(double X, double Y, double A)
        {
            double dX;
            double dY;
            MainForm.DisplayText("Nozzle.Move_m(): X= " + X.ToString(CultureInfo.InvariantCulture)
                + ", Y= " + Y.ToString(CultureInfo.InvariantCulture) + ", A= " + A.ToString(CultureInfo.InvariantCulture));
            if (!GetPositionCorrection_m(A, out dX, out dY))
            {
                return false;
            };
            double Xoff = MainForm.Setting.DownCam_NozzleOffsetX;
            double Yoff = MainForm.Setting.DownCam_NozzleOffsetY;
            return CNC_XYA(X + Xoff + dX, Y + Yoff + dY, A);
        }

        private bool CNC_A_m(double A)
        {
            return MainForm.CNC_A_m(A);
        }

        private bool CNC_XYA(double X, double Y, double A)
        {
            return MainForm.CNC_XYA_m(X, Y, A);
        }

    }
}
