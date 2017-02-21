﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IO.Thermo;
using Spectra;
using Chemistry;
using MathNet.Numerics.Statistics;
using Proteomics;
using MassSpectrometry;

namespace ProteoformSuiteInternal
{
    public class RawFileReader
    {
        //GET MS SCANS
        //GET MS SCANS
        public static void get_ms_scans(string filename, string raw_file_path)
        {
            using (ThermoDynamicData myMsDataFile = ThermoDynamicData.InitiateDynamicConnection(raw_file_path))
            {
                foreach (IMsDataScan<ThermoSpectrum> spectrum in myMsDataFile)
                {
                    MsScan scan = new ProteoformSuiteInternal.MsScan(spectrum.MsnOrder, spectrum.OneBasedScanNumber, filename, spectrum.RetentionTime, spectrum.RetentionTime, spectrum.TotalIonCurrent, spectrum.MassSpectrum.XArray, spectrum.MassSpectrum.YArray, spectrum.MassSpectrum.GetNoises());
                    Lollipop.Ms_scans.Add(scan);
                }
                //set charge, mz, intensity, find MS1 numbers
                foreach (TopDownHit hit in Lollipop.td_hits_calibration.Where(f => f.filename == filename).ToList())
                {
                    double mz = (myMsDataFile.GetOneBasedScan(hit.scan) as ThermoScanWithPrecursor).IsolationMz;
                    hit.charge = Convert.ToInt16(Math.Round(hit.reported_mass / (double)mz, 0)); //m / (m/z)  round to get charge 
                    hit.mz = hit.reported_mass.ToMz(hit.charge);
                }
            }
        }

        public static void check_fragmented_experimentals(List<InputFile> files)
        {
            foreach(InputFile file in files)
            {
                List<ExperimentalProteoform> experimentals = Lollipop.proteoform_community.experimental_proteoforms.Where(e => e.etd_match_count == 0 && e.aggregated_components.Where(c => c.input_file.filename.Replace("_calibrated","") == file.filename).Count() > 0).ToList();
                if (experimentals.Count > 0)
                {
                    using (ThermoDynamicData myMsDataFile = ThermoDynamicData.InitiateDynamicConnection(file.path + "\\" + file.filename + file.extension))
                    {
                        foreach (ExperimentalProteoform e in experimentals)
                        {
                            foreach (Component c in e.aggregated_components.Where(c => c.input_file.filename.Replace("_calibrated", "") == file.filename).ToList())
                            {
                                int min_MS1 = Convert.ToInt16(c.scan_range.Split('-')[0]);
                                int max_MS1 = Convert.ToInt16(c.scan_range.Split('-')[1]);
                                int scan_number = min_MS1;
                                IThermoScan scan = myMsDataFile.GetOneBasedScan(scan_number);
                                while (e.fragmented == false && (scan.OneBasedScanNumber <= max_MS1 || scan.MsnOrder == 2))
                                {
                                    scan = myMsDataFile.GetOneBasedScan(scan_number);
                                    if (scan.MsnOrder < 2) { scan_number++; continue; }
                                    foreach (ChargeState cs in c.charge_states)
                                    {
                                        if (Math.Abs(cs.mz_centroid - (scan as ThermoScanWithPrecursor).IsolationMz) <= 5) //checking around 5 m/z... might change to tolerance used? 
                                        {
                                            e.fragmented = true;
                                            continue;
                                        }
                                    }
                                    scan_number++;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}