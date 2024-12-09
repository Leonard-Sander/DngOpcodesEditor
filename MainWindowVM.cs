using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using ImageMagick;
using System.Windows.Controls;
using System.IO.Enumeration;

namespace DngOpcodesEditor;

public partial class MainWindowVM : ObservableObject
{
    [ObservableProperty]
    ObservableCollection<Opcode> _opcodes = new ObservableCollection<Opcode>();
    //[ObservableProperty]
    //Dictionary<Opcode, int> _opcodeLists = new Dictionary<Opcode, int>();
    [ObservableProperty]
    Opcode _selectedOpcode;
    [ObservableProperty]
    Image _imgSrc, _imgDst;
    [ObservableProperty]
    bool _encodeGamma, _decodeGamma;
    public Boolean StripLum = true;
    public Boolean ClipHighlights = true;
    public Boolean BGGRFix = false;
    public Boolean GRBGFix = false;
    public Boolean CleanUp = true;
    string SAMPLES_DIR = Path.Combine(Environment.CurrentDirectory, "Samples");
    public MainWindowVM()
    {
        EncodeGamma = true;
        SetWindowTitle();
        _opcodes.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                foreach (Opcode item in e.NewItems)
                {
                    item.PropertyChanged += (ps, pe) => ApplyOpcodes();
                }
            }
            if (!Opcodes.Contains(SelectedOpcode))
            {
                SelectedOpcode = Opcodes.LastOrDefault();
            }
        };
    }
    public void SetWindowTitle(string filename = "")
    {

        App.Current.MainWindow.Title = $"DNG Opcodes Editor v{Assembly.GetExecutingAssembly().GetName().Version.ToString()}";
        if (!string.IsNullOrWhiteSpace(filename))
        {
            App.Current.MainWindow.Title += $" - {Path.GetFileName(filename)}";
        }
    }
    public void OpenImage()
    {
        var dialog = new OpenFileDialog() { Filter = "All files (*.*)|*.*" };
        dialog.InitialDirectory = SAMPLES_DIR;
        if (dialog.ShowDialog() == true)
        {
            OpenImage(dialog.FileName);
        }
    }
    public void OpenImage(string filename)
    {
        ImgSrc = new Image();
        int bpp = ImgSrc.Open(filename);
        DecodeGamma = bpp <= 32 ? true : false;
        ImgDst = ImgSrc.Clone();
        SetWindowTitle(filename);
    }
    public void SaveImage()
    {
        var dialog = new SaveFileDialog() { Filter = "Tiff image (*.tiff)|*.tiff" };
        dialog.FileName = "processed_" + DateTime.Now.ToString("hhmmss") + ".tiff";
        if (dialog.ShowDialog() == true)
        {
            SaveImage(dialog.FileName);
        }
    }
    public void SaveImage(string filename) => ImgDst.SaveImage(filename);
    public void AddOpcode(OpcodeId id)
    {
        var header = new OpcodeHeader() { id = id };
        switch (id)
        {
            case OpcodeId.WarpRectilinear:
                Opcodes.Add(new OpcodeWarpRectilinear() { header = header });
                break;
            case OpcodeId.FixVignetteRadial:
                Opcodes.Add(new OpcodeFixVignetteRadial() { header = header });
                break;
            case OpcodeId.TrimBounds:
                Opcodes.Add(new OpcodeTrimBounds() { header = header });
                break;
            default:
                Opcodes.Add(new Opcode());
                break;
        }
    }
    public void ImportDng()
    {
        var dialog = new OpenFileDialog() { Multiselect = true, Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
        dialog.InitialDirectory = SAMPLES_DIR;
        if (dialog.ShowDialog() == true)
        {
            foreach (String fileName in dialog.FileNames)
            { 
                ImportDng(dialog.FileName); 
                Console.WriteLine(dialog.FileName);
            }
        }
    }
    public void ImportDng(string filename)
    {
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        // Import OpcodeList2 and OpcodeList3
        for (int listIndex = 2; listIndex < 4; listIndex++)
        {
            //int listIndex = 3;  // TODO: Add support for additional lists
            var ms = new MemoryStream();
            // -IFD0:OpcodeList
            var exifProcess = Process.Start(new ProcessStartInfo("exiftool.exe", $"-b -OpcodeList{listIndex} \"{filename}\"")
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            exifProcess.StandardOutput.BaseStream.CopyTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length > 0)
            {
                foreach (Opcode opcode in ImportBin(bytes))
                {
                    opcode.ListIndex = listIndex;
                    Opcodes.Add(opcode);
                }
                SelectedOpcode = Opcodes.Last();
            }
        }
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
    }

    public void ImportFlatField() 
    {
        //var dialog = new OpenFileDialog() { Filter = "TIFF files (*.tiff)|*.tiff|All files (*.*)|*.*" };
        var dialog = new OpenFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            ImportFlatField(dialog.FileName);
        }
    }
    public void ImportFlatField(string filename) 
    {
        var dngValReadProcess = Process.Start(new ProcessStartInfo("dng_validate.exe", $"-max 0 -dng \"{filename + "_valid.dng"}\" \"{filename}\"")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true
        });
        dngValReadProcess.WaitForExit();

        var dcrawProcess = Process.Start(new ProcessStartInfo("dcraw.exe", $"-D -4 -w -T \"{filename + "_valid.dng"}\"")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true
        });
        dcrawProcess.WaitForExit();
        
        using (var image = new MagickImage(filename + "_valid.tiff"))
        {
            Console.WriteLine($"Width: {image.Width}, Height: {image.Height}, ColorType: {image.ColorType}, Bitdepth: {image.Depth}, Path: {filename}");

            var maxValueB = image.GetPixels().GetPixel(0,0).GetChannel(0);            
            var maxValueG1 = image.GetPixels().GetPixel(1,0).GetChannel(0);
            var maxValueG2 = image.GetPixels().GetPixel(0,1).GetChannel(0);
            var maxValueR = image.GetPixels().GetPixel(1,1).GetChannel(0);

            var imagePixels = image.GetPixels();

            for (int j = 0; j < image.Height / 2; j++)
            {            
                for (int i = 0; i < image.Width / 2; i++)
                {                    
                    //Console.Write(image.GetPixels().GetPixel(i,j).GetChannel(0) + ", ");
                    if (imagePixels.GetPixel(i*2,j*2).GetChannel(0) > maxValueB)
                    {
                        maxValueB = imagePixels.GetPixel(i*2,j*2).GetChannel(0);   
                    } 
                    if (imagePixels.GetPixel(i*2+1,j*2).GetChannel(0) > maxValueG1)
                    {
                        maxValueG1 = imagePixels.GetPixel(i*2+1,j*2).GetChannel(0);   
                    } 
                    if (imagePixels.GetPixel(i*2,j*2+1).GetChannel(0) > maxValueG2)
                    {
                        maxValueG2 = imagePixels.GetPixel(i*2,j*2+1).GetChannel(0);   
                    } 
                    if (imagePixels.GetPixel(i*2+1,j*2+1).GetChannel(0) > maxValueR)
                    {
                        maxValueR = imagePixels.GetPixel(i*2+1,j*2+1).GetChannel(0);   
                    }                     
                }
                //Console.WriteLine(maxValueB + ", " + maxValueG1 + ", " + maxValueG2 + ", " + maxValueR);          
            }

            Console.WriteLine("maxValueB: " + maxValueB);
            Console.WriteLine("maxValueG1: " + maxValueG1);
            Console.WriteLine("maxValueG2: " + maxValueG2);
            Console.WriteLine("maxValueR: " + maxValueR);
            maxValueG1 = Math.Max(maxValueG1,maxValueG2);

            /*var gainMapBlue = new OpcodeGainMap() {
                top = 0, left = 0, ListIndex = 2, bottom = (uint)image.Height, right = (uint)image.Width, plane = 0, planes = 1, rowPitch = 2, colPitch = 2, 
                mapPointsH = (uint)image.Width/2, mapPointsV = (uint)image.Height/2, mapSpacingH = 0, mapOriginH = 0, mapPlanes = 1, 
                mapGains = new float[image.Width/2*image.Height/2], header = new OpcodeHeader(),};  

            var gainMapGreen1 = new OpcodeGainMap() {
                top = 0, left = 1, ListIndex = 2, bottom = (uint)image.Height, right = (uint)image.Width, plane = 0, planes = 1, rowPitch = 2, colPitch = 2, 
                mapPointsH = (uint)image.Width/2, mapPointsV = (uint)image.Height/2, mapSpacingH = 0, mapOriginH = 0, mapPlanes = 1,
                mapGains = new float[image.Width/2*image.Height/2], header = new OpcodeHeader(),};  

            var gainMapGreen2 = new OpcodeGainMap() {
                top = 1, left = 0, ListIndex = 2, bottom = (uint)image.Height, right = (uint)image.Width, plane = 0, planes = 1, rowPitch = 2, colPitch = 2, 
                mapPointsH = (uint)image.Width/2, mapPointsV = (uint)image.Height/2, mapSpacingH = 0, mapOriginH = 0, mapPlanes = 1,
                mapGains = new float[image.Width/2*image.Height/2], header = new OpcodeHeader(),};  

            var gainMapRed = new OpcodeGainMap() {
                top = 1, left = 1, ListIndex = 2, bottom = (uint)image.Height, right = (uint)image.Width, plane = 0, planes = 1, rowPitch = 2, colPitch = 2, 
                mapPointsH = (uint)image.Width/2, mapPointsV = (uint)image.Height/2, mapSpacingH = 0, mapOriginH = 0, mapPlanes = 1,
                mapGains = new float[image.Width/2*image.Height/2], header = new OpcodeHeader(),};  

            gainMapBlue.header.id = OpcodeId.GainMap;
            gainMapGreen1.header.id = OpcodeId.GainMap;
            gainMapGreen2.header.id = OpcodeId.GainMap;
            gainMapRed.header.id = OpcodeId.GainMap;

            Opcodes.Add(gainMapBlue);
            Opcodes.Add(gainMapGreen1);
            Opcodes.Add(gainMapGreen2);
            Opcodes.Add(gainMapRed);*/

            var gains = new float[image.Height * image.Width];
            var lumGains = new float[image.Height * image.Width / 4];

            for (int j = 0; j < image.Height / 2; j++)
            {            
                for (int i = 0; i < image.Width / 2; i++)
                {                    
                    var minGain = Math.Min(Math.Min((float) maxValueB / imagePixels.GetPixel(i*2,j*2).GetChannel(0), 
                                                    (float) maxValueG1 / imagePixels.GetPixel(i*2+1,j*2).GetChannel(0)),
                                           Math.Min((float) maxValueG2 / imagePixels.GetPixel(i*2,j*2+1).GetChannel(0), 
                                                    (float) maxValueR / imagePixels.GetPixel(i*2+1,j*2+1).GetChannel(0)));

                    /*gainMapBlue.mapGains[j * image.Width / 2 + i] = (float) maxValueB / image.GetPixels().GetPixel(i*2,j*2).GetChannel(0);
                    gainMapGreen1.mapGains[j * image.Width / 2 + i] = (float) maxValueG1 / image.GetPixels().GetPixel(i*2+1,j*2).GetChannel(0);
                    gainMapGreen2.mapGains[j * image.Width / 2 + i] = (float) maxValueG2 / image.GetPixels().GetPixel(i*2,j*2+1).GetChannel(0);
                    gainMapRed.mapGains[j * image.Width / 2 + i] = (float) maxValueR / image.GetPixels().GetPixel(i*2+1,j*2+1).GetChannel(0);

                    var minGain2 = Math.Min(Math.Min(gainMapBlue.mapGains[j * image.Width / 2 + i],
                                                     gainMapGreen1.mapGains[j * image.Width / 2 + i]),
                                            Math.Min(gainMapGreen2.mapGains[j * image.Width / 2 + i],
                                                     gainMapRed.mapGains[j * image.Width / 2 + i]));

                    gainMapBlue.mapGains[j * image.Width / 2 + i] = gainMapBlue.mapGains[j * image.Width / 2 + i] / minGain2;
                    gainMapGreen1.mapGains[j * image.Width / 2 + i] = gainMapGreen1.mapGains[j * image.Width / 2 + i] / minGain2;
                    gainMapGreen2.mapGains[j * image.Width / 2 + i] = gainMapGreen2.mapGains[j * image.Width / 2 + i] / minGain2;
                    gainMapRed.mapGains[j * image.Width / 2 + i] = gainMapRed.mapGains[j * image.Width / 2 + i] / minGain2;*/
                    
                    gains[i*2     + j*2     * image.Width] = (float) maxValueB / imagePixels.GetPixel(i*2,j*2).GetChannel(0);
                    gains[i*2+1   + j*2     * image.Width] = (float) maxValueG1 / imagePixels.GetPixel(i*2+1,j*2).GetChannel(0);
                    gains[i*2     + (j*2+1) * image.Width] = (float) maxValueG2 / imagePixels.GetPixel(i*2,j*2+1).GetChannel(0);
                    gains[i*2+1   + (j*2+1) * image.Width] = (float) maxValueR / imagePixels.GetPixel(i*2+1,j*2+1).GetChannel(0);

                    lumGains[i + j * image.Width / 2] = Math.Min(Math.Min(gains[i*2     + j*2     * image.Width],
                                                                          gains[i*2+1   + j*2     * image.Width]),
                                                                 Math.Min(gains[i*2     + (j*2+1) * image.Width],
                                                                          gains[i*2+1   + (j*2+1) * image.Width]));

                    if(StripLum)
                    {
                        gains[i*2     + j*2     * image.Width] = gains[i*2     + j*2     * image.Width] / lumGains[i + j * image.Width / 2];
                        gains[i*2+1   + j*2     * image.Width] = gains[i*2+1   + j*2     * image.Width] / lumGains[i + j * image.Width / 2];
                        gains[i*2     + (j*2+1) * image.Width] = gains[i*2     + (j*2+1) * image.Width] / lumGains[i + j * image.Width / 2];
                        gains[i*2+1   + (j*2+1) * image.Width] = gains[i*2+1   + (j*2+1) * image.Width] / lumGains[i + j * image.Width / 2];
                    }                    
                    
                    imagePixels.GetPixel(i*2,j*2).SetChannel(0,     (ushort)(imagePixels.GetPixel(i * 2, j * 2).GetChannel(0) * minGain));                    
                    imagePixels.GetPixel(i*2+1,j*2).SetChannel(0,   (ushort)(imagePixels.GetPixel(i * 2 + 1, j * 2).GetChannel(0) * minGain));
                    imagePixels.GetPixel(i*2,j*2+1).SetChannel(0,   (ushort)(imagePixels.GetPixel(i * 2, j * 2 + 1).GetChannel(0) * minGain));
                    imagePixels.GetPixel(i*2+1,j*2+1).SetChannel(0, (ushort)(imagePixels.GetPixel(i * 2 + 1, j * 2 + 1).GetChannel(0) * minGain));
                }
            }

            if(!File.Exists(filename + "_chroma.dng"))
            {
                image.Write(filename + "_chroma.tiff");
                File.Copy(filename + "_chroma.tiff", filename + "_chroma.dng");
                var exifProcess = Process.Start(new ProcessStartInfo(
                    "exiftool.exe", 
                    $"-n -IFD0:SubfileType#=0 -overwrite_original -tagsfromfile \"{filename + "_valid.dng"}\" \"-all:all>all:all\" -DNGVersion -DNGBackwardVersion -ColorMatrix1 -ColorMatrix2 \"-IFD0:BlackLevelRepeatDim<SubIFD:BlackLevelRepeatDim\" \"-IFD0:PhotometricInterpretation<SubIFD:PhotometricInterpretation\" \"-IFD0:CalibrationIlluminant1<SubIFD:CalibrationIlluminant1\" \"-IFD0:CalibrationIlluminant2<SubIFD:CalibrationIlluminant2\" -SamplesPerPixel \"-IFD0:CFARepeatPatternDim<SubIFD:CFARepeatPatternDim\" \"-IFD0:CFAPattern2<SubIFD:CFAPattern2\" -AsShotNeutral \"-IFD0:ActiveArea<SubIFD:ActiveArea\" \"-IFD0:DefaultScale<SubIFD:DefaultScale\" \"-IFD0:DefaultCropOrigin<SubIFD:DefaultCropOrigin\" \"-IFD0:DefaultCropSize<SubIFD:DefaultCropSize\" \"-IFD0:OpcodeList1<SubIFD:OpcodeList1\" \"-IFD0:OpcodeList2<SubIFD:OpcodeList2\" \"-IFD0:OpcodeList3<SubIFD:OpcodeList3\"  \"{filename + "_chroma.dng"}\"") //!exposureTag!
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                exifProcess = Process.Start(new ProcessStartInfo(
                    "exiftool.exe", 
                    $"-overwrite_original -tagsfromfile \"{filename + "_valid.dng"}\" \"-IFD0:AnalogBalance\" \"-IFD0:ColorMatrix1\" \"-IFD0:ColorMatrix2\" \"-IFD0:CameraCalibration1\" \"-IFD0:CameraCalibration2\" \"-IFD0:AsShotNeutral\" \"-IFD0:BaselineExposure\" \"-IFD0:CalibrationIlluminant1\" \"-IFD0:CalibrationIlluminant2\" \"-IFD0:ForwardMatrix1\" \"-IFD0:ForwardMatrix2\" \"{filename + "_chroma.dng"}\"")
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                exifProcess.WaitForExit();
                var dngValWriteProcess = Process.Start(new ProcessStartInfo("dng_validate.exe", $"-v -dng \"{filename + "_valid.dng"}\" \"{filename + "_chroma.dng"}\"")
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                dngValWriteProcess.WaitForExit();
            }          

            if(CleanUp)
            {
                File.Delete(filename + "_valid.dng"); 
                File.Delete(filename + "_valid.tiff"); 
                File.Delete(filename + "_chroma.tiff");
            }
            else
            {
                for (int j = 0; j < image.Height / 2; j++)
                {            
                    for (int i = 0; i < image.Width / 2; i++)
                    {                    
                        imagePixels.GetPixel(i*2,j*2).SetChannel(0,     (ushort)(maxValueB / lumGains[i + j * image.Width / 2]));                    
                        imagePixels.GetPixel(i*2+1,j*2).SetChannel(0,   (ushort)(maxValueG1 / lumGains[i + j * image.Width / 2]));
                        imagePixels.GetPixel(i*2,j*2+1).SetChannel(0,   (ushort)(maxValueG2 / lumGains[i + j * image.Width / 2]));
                        imagePixels.GetPixel(i*2+1,j*2+1).SetChannel(0, (ushort)(maxValueR / lumGains[i + j * image.Width / 2]));
                        //Console.WriteLine(lumGains[i + j * image.Width / 2]);
                    }
                }

                image.Write(filename + "_luma.tiff");
                File.Copy(filename + "_luma.tiff", filename + "_luma.dng");
                var exifProcess = Process.Start(new ProcessStartInfo(
                    "exiftool.exe", 
                    $"-n -IFD0:SubfileType#=0 -overwrite_original -tagsfromfile \"{filename + "_valid.dng"}\" \"-all:all>all:all\" -DNGVersion -DNGBackwardVersion -ColorMatrix1 -ColorMatrix2 \"-IFD0:BlackLevelRepeatDim<SubIFD:BlackLevelRepeatDim\" \"-IFD0:PhotometricInterpretation<SubIFD:PhotometricInterpretation\" \"-IFD0:CalibrationIlluminant1<SubIFD:CalibrationIlluminant1\" \"-IFD0:CalibrationIlluminant2<SubIFD:CalibrationIlluminant2\" -SamplesPerPixel \"-IFD0:CFARepeatPatternDim<SubIFD:CFARepeatPatternDim\" \"-IFD0:CFAPattern2<SubIFD:CFAPattern2\" -AsShotNeutral \"-IFD0:ActiveArea<SubIFD:ActiveArea\" \"-IFD0:DefaultScale<SubIFD:DefaultScale\" \"-IFD0:DefaultCropOrigin<SubIFD:DefaultCropOrigin\" \"-IFD0:DefaultCropSize<SubIFD:DefaultCropSize\" \"-IFD0:OpcodeList1<SubIFD:OpcodeList1\" \"-IFD0:OpcodeList2<SubIFD:OpcodeList2\" \"-IFD0:OpcodeList3<SubIFD:OpcodeList3\"  \"{filename + "_luma.dng"}\"") //!exposureTag!
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                exifProcess.WaitForExit();
                exifProcess = Process.Start(new ProcessStartInfo(
                    "exiftool.exe", 
                    $"-overwrite_original -tagsfromfile \"{filename + "_valid.dng"}\" \"-IFD0:AnalogBalance\" \"-IFD0:ColorMatrix1\" \"-IFD0:ColorMatrix2\" \"-IFD0:CameraCalibration1\" \"-IFD0:CameraCalibration2\" \"-IFD0:AsShotNeutral\" \"-IFD0:BaselineExposure\" \"-IFD0:CalibrationIlluminant1\" \"-IFD0:CalibrationIlluminant2\" \"-IFD0:ForwardMatrix1\" \"-IFD0:ForwardMatrix2\" \"{filename + "_luma.dng"}\"")
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                exifProcess.WaitForExit();
                var dngValWriteProcess = Process.Start(new ProcessStartInfo("dng_validate.exe", $"-v -dng \"{filename + "_valid.dng"}\" \"{filename + "_luma.dng"}\"")
                {
                    UseShellExecute = true,
                    CreateNoWindow = false
                });
                dngValWriteProcess.WaitForExit();
            }
                        

            var dialog = new OpenFileDialog() { Multiselect = true, Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                foreach (var fileName in dialog.FileNames)
                {
                    dngValReadProcess = Process.Start(new ProcessStartInfo("dng_validate.exe", $"-max 0 -dng \"{fileName + "_valid.dng"}\" \"{fileName}\"")
                    {
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    dngValReadProcess.WaitForExit();

                    dcrawProcess = Process.Start(new ProcessStartInfo("dcraw.exe", $"-D -4 -w -T \"{fileName + "_valid.dng"}\"")
                    {
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    dcrawProcess.WaitForExit();
                    
                    using (var output = new MagickImage(fileName + "_valid.tiff"))
                    {
                        var outputPixels = output.GetPixels();
                        var maxValue = 0f;

                        if(!ClipHighlights)
                        {
                            for (int j = 0; j < image.Height / 2; j++)
                            {            
                                for (int i = 0; i < image.Width / 2; i++)
                                {                    
                                    var localMaxValue = Math.Max(Math.Max(outputPixels.GetPixel(i*2, j*2).GetChannel(0) *    gains[i*2     + j*2     * image.Width],
                                                                        outputPixels.GetPixel(i*2+1, j*2).GetChannel(0) *  gains[i*2+1   + j*2     * image.Width]),
                                                                Math.Max(outputPixels.GetPixel(i*2, j*2+1).GetChannel(0) *  gains[i*2     + (j*2+1) * image.Width],
                                                                        outputPixels.GetPixel(i*2+1, j*2+1).GetChannel(0) *gains[i*2+1   + (j*2+1) * image.Width]));
                                    if (maxValue < localMaxValue) 
                                    {
                                        maxValue = localMaxValue;
                                    }                       
                                }
                            }
                        }
                        
                        if (ClipHighlights || maxValue <= 65535)
                        {
                            for (int j = 0; j < image.Height / 2; j++)
                            {            
                                for (int i = 0; i < image.Width / 2; i++)
                                {                    
                                    outputPixels.GetPixel(i*2,   j*2  ).SetChannel(0, (ushort) Math.Min(65535,outputPixels.GetPixel(i*2,   j*2  ).GetChannel(0) * gains[i*2   + j*2     * image.Width]));
                                    outputPixels.GetPixel(i*2+1, j*2  ).SetChannel(0, (ushort) Math.Min(65535,outputPixels.GetPixel(i*2+1, j*2  ).GetChannel(0) * gains[i*2+1 + j*2     * image.Width]));
                                    outputPixels.GetPixel(i*2,   j*2+1).SetChannel(0, (ushort) Math.Min(65535,outputPixels.GetPixel(i*2,   j*2+1).GetChannel(0) * gains[i*2   + (j*2+1) * image.Width]));
                                    outputPixels.GetPixel(i*2+1, j*2+1).SetChannel(0, (ushort) Math.Min(65535,outputPixels.GetPixel(i*2+1, j*2+1).GetChannel(0) * gains[i*2+1 + (j*2+1) * image.Width]));                            
                                }
                            }
                            output.Write(fileName + "_65535.tiff");
                            Console.WriteLine("Written " + fileName + " with Whitelevel 65535");
                        }
                        else    //unreachable so far
                        {
                            for (int j = 0; j < image.Height / 2; j++)
                            {            
                                for (int i = 0; i < image.Width / 2; i++)
                                {                    
                                    outputPixels.GetPixel(i*2,j*2).SetChannel(0,    (ushort)(outputPixels.GetPixel(i*2, j*2).GetChannel(0) *    gains[i*2     + j*2     * image.Width] / maxValue * 65535));
                                    outputPixels.GetPixel(i*2+1,j*2).SetChannel(0,  (ushort)(outputPixels.GetPixel(i*2+1, j*2).GetChannel(0) *  gains[i*2+1   + j*2     * image.Width] / maxValue * 65535));
                                    outputPixels.GetPixel(i*2,j*2+1).SetChannel(0,  (ushort)(outputPixels.GetPixel(i*2, j*2+1).GetChannel(0) *  gains[i*2     + (j*2+1) * image.Width] / maxValue * 65535));
                                    outputPixels.GetPixel(i*2+1,j*2+1).SetChannel(0,(ushort)(outputPixels.GetPixel(i*2+1, j*2+1).GetChannel(0) *gains[i*2+1   + (j*2+1) * image.Width] / maxValue * 65535));                            
                                }
                            }
                            output.Write(fileName + "_" + (ushort)(65535 * (65535 / maxValue)) + ".tiff");
                            Console.WriteLine("Written " + fileName + " with Whitelevel " + (ushort)(65535 * (65535 / maxValue)));
                        }
                        File.Copy(fileName + "_65535.tiff", fileName + "_65535.dng");
                        var exifProcess = Process.Start(new ProcessStartInfo(
                            "exiftool.exe", 
                            $"-n -IFD0:SubfileType#=0 -overwrite_original -tagsfromfile \"{fileName + "_valid.dng"}\" \"-all:all>all:all\" -DNGVersion -DNGBackwardVersion -ColorMatrix1 -ColorMatrix2 \"-IFD0:BlackLevelRepeatDim<SubIFD:BlackLevelRepeatDim\" \"-IFD0:PhotometricInterpretation<SubIFD:PhotometricInterpretation\" \"-IFD0:CalibrationIlluminant1<SubIFD:CalibrationIlluminant1\" \"-IFD0:CalibrationIlluminant2<SubIFD:CalibrationIlluminant2\" -SamplesPerPixel \"-IFD0:CFARepeatPatternDim<SubIFD:CFARepeatPatternDim\" \"-IFD0:CFAPattern2<SubIFD:CFAPattern2\" -AsShotNeutral \"-IFD0:ActiveArea<SubIFD:ActiveArea\" \"-IFD0:DefaultScale<SubIFD:DefaultScale\" \"-IFD0:DefaultCropOrigin<SubIFD:DefaultCropOrigin\" \"-IFD0:DefaultCropSize<SubIFD:DefaultCropSize\" \"-IFD0:OpcodeList1<SubIFD:OpcodeList1\" \"-IFD0:OpcodeList2<SubIFD:OpcodeList2\" \"-IFD0:OpcodeList3<SubIFD:OpcodeList3\"  \"{fileName + "_65535.dng"}\"") //!exposureTag!
                        {
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        });
                        exifProcess.WaitForExit();
                        exifProcess = Process.Start(new ProcessStartInfo(
                            "exiftool.exe", 
                            $"-overwrite_original -tagsfromfile \"{fileName + "_valid.dng"}\" \"-IFD0:AnalogBalance\" \"-IFD0:ColorMatrix1\" \"-IFD0:ColorMatrix2\" \"-IFD0:CameraCalibration1\" \"-IFD0:CameraCalibration2\" \"-IFD0:AsShotNeutral\" \"-IFD0:BaselineExposure\" \"-IFD0:CalibrationIlluminant1\" \"-IFD0:CalibrationIlluminant2\" \"-IFD0:ForwardMatrix1\" \"-IFD0:ForwardMatrix2\" \"{fileName + "_65535.dng"}\"")
                        {
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        });
                        exifProcess.WaitForExit();
                        var dngValWriteProcess = Process.Start(new ProcessStartInfo("dng_validate.exe", $"-v -dng \"{fileName + "_valid.dng"}\" \"{fileName + "_65535.dng"}\"")
                        {
                            UseShellExecute = true,
                            CreateNoWindow = false
                        });
                        dngValWriteProcess.WaitForExit();

                        if(CleanUp)
                        {
                            File.Delete(fileName + "_valid.dng"); 
                            File.Delete(fileName + "_valid.tiff"); 
                            File.Delete(fileName + "_65535.tiff");
                            /*var directory = fileName;
                            for (int i = directory.Length - 1; i > -1; i--)
                            {
                                directory = directory.Remove(directory.Length - 1);                                
                                if(directory.Last().ToString() == "\\")
                                {                                    
                                    File.Move(fileName, directory + "\\original" ); 
                                }
                            }      */ 
                            File.Copy(fileName, fileName + ".dng_original");   
                            File.Delete(fileName);
                            File.Copy(fileName + "_65535.dng", fileName);   
                            File.Delete(fileName + "_65535.dng");      
                        }
                    }
                }
            }    
        }
    }
    public void ImportBin()
    {
        var dialog = new OpenFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
        dialog.InitialDirectory = SAMPLES_DIR;
        if (dialog.ShowDialog() == true)
        {
            ImportBin(dialog.FileName);
        }
    }
    public void ImportBin(string filename)
    {
        foreach (var opcode in ImportBin(File.ReadAllBytes(filename)))
        {
            Opcodes.Add(opcode);
        }
        SelectedOpcode = Opcodes.Last();
    }
    
    public void BatchDNG()
    {
        if ((StripLum | BGGRFix | GRBGFix) && !(BGGRFix && GRBGFix))
        {
            var dialog = new OpenFileDialog() { Multiselect = true, Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                BatchDNG(dialog.FileNames);
            }   
        }
        else 
        {
            Console.WriteLine("Illegal or no Operation selected.");
        }             
    }

    public void BatchDNG(String[] files)        
    {
        Opcodes.Clear();
        if(StripLum)
        {
            foreach (string file in files)      //determines global minimum Gain of all GainMaps of all selected Frames
            {
                if (Path.GetExtension(file) == ".dng") 
                {
                    ImportDng(file);
                    Console.WriteLine(file);
                }            
            }
            var minGain = MinValueBatch();
            Opcodes.Clear();
            foreach (string file in files)      
            {
                if (Path.GetExtension(file) == ".dng") {
                    ImportDng(file);
                    //ImportDng(file);                    //Import twice to have GainMap Object left to write stripped Luma in Opcode3
                    StripVigLum(minGain);         
                    ExportDNG(file);                    
                    Opcodes.Clear();                 
                }            
            }
        }
        else
        {
            foreach (string file in files)      
            {
                if (Path.GetExtension(file) == ".dng") {
                    ImportDng(file);
                    FixMismatch();
                    ExportDNG(file);                    
                    Opcodes.Clear();                    
                }            
            }
        }
        
    }

    public float MinValueBatch()
    {
        var gainMapOpcodes = new List<OpcodeGainMap>();
        foreach (var opcode in Opcodes)
        {
            if (opcode.header.id == OpcodeId.GainMap){ gainMapOpcodes.Add(opcode as OpcodeGainMap); }
        }
        Console.WriteLine(gainMapOpcodes.Count + " GainMaps loaded");
        if (gainMapOpcodes.Count > 0)
        {
            var minGain = 10.0f;
            foreach (var gainMapOpcode in gainMapOpcodes)
            {
                foreach (var mapGain in gainMapOpcode.mapGains)
                {
                    if (mapGain < minGain) { minGain = mapGain; }
                }
            }
            Console.WriteLine("Minimum Gain of all imported DNG: " + minGain);
            
            return minGain;
        }   
        return 1.0f;
    }

    public void StripVigLum(float globalMinGain)
    {
        //Opcodes.RemoveAt(Opcodes.Count - 1); Opcodes.RemoveAt(Opcodes.Count - 1); Opcodes.RemoveAt(Opcodes.Count - 1);
        //Opcodes[4].ListIndex = 3;
        var gainMapOpcodes = new List<OpcodeGainMap>();
        foreach (var opcode in Opcodes)
        {
            if (opcode.header.id == OpcodeId.GainMap && opcode.ListIndex == 2){ gainMapOpcodes.Add(opcode as OpcodeGainMap); }
        }
        if (gainMapOpcodes.Count == 4)
        {        
            var minGain = 10.0f;
            foreach (var gainMapOpcode in gainMapOpcodes)
            {
                foreach (var mapGain in gainMapOpcode.mapGains)
                {
                    if (mapGain < minGain) { minGain = mapGain; }
                }
            }
            Console.WriteLine("Minimum Gain in current Frame: " + minGain);
            var minGains = new List<float>();
            for (int i = 0; i < gainMapOpcodes[0].mapGains.Count(); i++)
            {
                minGains.Add(MathF.Min(MathF.Min(gainMapOpcodes[0].mapGains[i], gainMapOpcodes[1].mapGains[i]), 
                                        MathF.Min(gainMapOpcodes[2].mapGains[i], gainMapOpcodes[3].mapGains[i])));
                Console.WriteLine("Uncorrected minimum Gain at " + i + " is " + minGains[i]);
                for (int j = 0; j < 4; j++)
                {
                    gainMapOpcodes[j].mapGains[i] = gainMapOpcodes[j].mapGains[i] / minGains[i];        //stripping luminance
                    if (globalMinGain < minGain)                                                        //not sure if this is nessesary
                    {
                        gainMapOpcodes[j].mapGains[i] = gainMapOpcodes[j].mapGains[i] * minGain;
                        gainMapOpcodes[j].mapGains[i] = gainMapOpcodes[j].mapGains[i] / globalMinGain;
                        Console.WriteLine("Doing Import for each DNG has a reason. Maybe.");
                    }                    
                }
                //var tmp = Opcodes[4] as OpcodeGainMap;
                //tmp.mapGains[i] = minGains[i] / globalMinGain;                                          //write luminance gain to extra gainmap
                //Opcodes[4] = tmp;                                                                       //not sure if this is nessesary         
                Console.WriteLine(
                        "Corrected Gains at " + i + " are " + gainMapOpcodes[0].mapGains[i] + 
                        " | " + gainMapOpcodes[1].mapGains[i] + " | " + gainMapOpcodes[2].mapGains[i] + 
                        " | " + gainMapOpcodes[3].mapGains[i]);                
            }
            if (BGGRFix) {                                                               //probably only specific to MotionCam Tools Exports
                var tmp = gainMapOpcodes[0].mapGains;
                gainMapOpcodes[0].mapGains = gainMapOpcodes[3].mapGains;
                gainMapOpcodes[3].mapGains = tmp;
            }   
            if (GRBGFix) {                                                               //probably only specific to MotionCam Tools Exports
                var tmp = gainMapOpcodes[0].mapGains;
                gainMapOpcodes[0].mapGains = gainMapOpcodes[1].mapGains;
                gainMapOpcodes[1].mapGains = tmp;
                tmp = gainMapOpcodes[2].mapGains;
                gainMapOpcodes[2].mapGains = gainMapOpcodes[3].mapGains;
                gainMapOpcodes[3].mapGains = tmp;
            }              
        }   

    }
    public void FixMismatch()
    {
        var gainMapOpcodes = new List<OpcodeGainMap>();
        foreach (var opcode in Opcodes)
        {
            if (opcode.header.id == OpcodeId.GainMap && opcode.ListIndex == 2){ gainMapOpcodes.Add(opcode as OpcodeGainMap); }
        }
        if (gainMapOpcodes.Count == 4)
        {        
            if (BGGRFix) {                                                               //probably only specific to MotionCam Tools Exports
                var tmp = gainMapOpcodes[0].mapGains;
                gainMapOpcodes[0].mapGains = gainMapOpcodes[3].mapGains;
                gainMapOpcodes[3].mapGains = tmp;
            }   
            if (GRBGFix) {                                                               //probably only specific to MotionCam Tools Exports
                var tmp = gainMapOpcodes[0].mapGains;
                gainMapOpcodes[0].mapGains = gainMapOpcodes[1].mapGains;
                gainMapOpcodes[1].mapGains = tmp;
                tmp = gainMapOpcodes[2].mapGains;
                gainMapOpcodes[2].mapGains = gainMapOpcodes[3].mapGains;
                gainMapOpcodes[3].mapGains = tmp;
            }              
        }  
    }
    public void ExportBin()
    {
        var dialog = new SaveFileDialog() { Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllBytes(dialog.FileName, new OpcodesWriter().WriteOpcodeList(Opcodes));
        }
    }
    public void ExportDNG()
    {
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        // OpcodeList1: applied as read directly from the file
        // OpcodeList2: applied after mapping to linear reference values
        // OpcodeList3: applied after demosaicing
        var dialog = new SaveFileDialog() { Filter = "DNG files (*.dng)|*.dng|All files (*.*)|*.*" };
        dialog.InitialDirectory = Environment.CurrentDirectory;
        if (dialog.ShowDialog() == true)
        {
            string tmpFile = "tmpDngOpcodesEditor.bin";
            var bytes = new OpcodesWriter().WriteOpcodeList(Opcodes);
            File.WriteAllBytes(tmpFile, bytes);
            // TODO: Add support for all OpcodeList
            //string tag = "OpcodeList2";         // default SubIFD
            string tag = "IFD0:OpcodeList2";
            var exifProcess = Process.Start(new ProcessStartInfo("exiftool.exe", $"-overwrite_original -n \"-{tag}#<={tmpFile}\" \"{dialog.FileName}\"")
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            exifProcess.WaitForExit();
            File.Delete(tmpFile);
        }
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
    }

    public void ExportDNG(String fileName)
    {
        string tmpFile = "tmpDngOpcodesEditor.bin";
        var bytes = new OpcodesWriter().WriteOpcodeList(Opcodes);
        File.WriteAllBytes(tmpFile, bytes);
        // TODO: Add support for all OpcodeList
        //string tag = "OpcodeList2";         // default SubIFD
        string tag = "IFD0:OpcodeList2";
        var exifProcess = Process.Start(new ProcessStartInfo("exiftool.exe", $" -v -n \"-{tag}#<={tmpFile}\" \"{fileName}\"")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false
        });
        string output = exifProcess.StandardOutput.ReadToEnd();
        Console.WriteLine("exif output: " + output);
        exifProcess.WaitForExit();
        File.Delete(tmpFile);
    }
    Opcode[] ImportBin(byte[] binaryData)
    {
        return new OpcodesReader().ReadOpcodeList(binaryData);
    }
    public void ApplyOpcodes()
    {
        Debug.WriteLine("ApplyOpcodes started");
        var sw = Stopwatch.StartNew();

        if (ImgSrc == null)
            return;

        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        ImgDst = ImgSrc.Clone();
        if (DecodeGamma)
        {
            var swGamma = Stopwatch.StartNew();
            // Apply gamma decoding
            Parallel.For(0, ImgDst.Height, (y) =>
            {
                for (int x = 0; x < ImgDst.Width; x++)
                {
                    ImgDst.ChangeRgb16Pixel(x, y, (pixel => MathF.Pow(pixel / 65535.0f, 2.2f) * 65535.0f));
                }
            });
            Debug.WriteLine($"\tGamma decoding executed in {swGamma.ElapsedMilliseconds}ms");
        }
        foreach (var opcode in Opcodes)
        {
            if (!opcode.Enabled)
                continue;
            switch (opcode.header.id)
            {
                case OpcodeId.WarpRectilinear:
                    OpcodesImplementation.WarpRectilinear(ImgDst, opcode as OpcodeWarpRectilinear);
                    break;
                case OpcodeId.FixVignetteRadial:
                    OpcodesImplementation.FixVignetteRadial(ImgDst, opcode as OpcodeFixVignetteRadial);
                    break;
                case OpcodeId.TrimBounds:
                    OpcodesImplementation.TrimBounds(ImgDst, opcode as OpcodeTrimBounds);
                    break;
                case OpcodeId.GainMap:
                    OpcodesImplementation.GainMap(ImgDst, opcode as OpcodeGainMap);
                    break;
                default:
                    Debug.WriteLine($"\t{opcode.header.id} not implemented yet and skipped");
                    continue;
            }
        }
        if (EncodeGamma)
        {
            var swGamma = Stopwatch.StartNew();
            // Apply gamma encoding
            Parallel.For(0, ImgDst.Height, (y) =>
            {
                for (int x = 0; x < ImgDst.Width; x++)
                {
                    ImgDst.ChangeRgb16Pixel(x, y, (pixel => MathF.Pow(pixel / 65535.0f, 1.0f / 2.2f) * 65535.0f));
                }
            });
            Debug.WriteLine($"\tGamma encoding executed in {swGamma.ElapsedMilliseconds}ms");
        }
        ImgDst.Update();
        Debug.WriteLine($"ApplyOpcodes executed in {sw.ElapsedMilliseconds}ms");
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
    }
}