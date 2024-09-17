using ImageProcessor;
using Microsoft.Win32;
using Python.Runtime; // Ensure this namespace is recognized without errors
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        #region Private Variables

        private string _selectedImageFileName;  // filename with file type

        private string _selectedImageName;      // filename without file type

        private string _pythonOutput; // for troubleshooting Python script outputs

        private string _weakAreaCamoDescription; // for displaying the text file output for EfficientDet-D7

        private BitmapImage _selectedImage;

        private BitmapImage _previewImage;

        private BitmapImage _facePredictionImage;

        private BitmapImage _ranknetFixationImage;

        private BitmapImage _ranknetCamouflageImage;

        private BitmapImage _weakAreaCamoImage;

        private readonly ICommand _exitCommand;

        private readonly ICommand _resetCommand;

        private readonly ICommand _saveCommand;

        private readonly ICommand _openCommand;

        private readonly ICommand _browseCommand;

        private readonly ICommand _selectedImageCommand;

        private readonly ICommand _runCommand;

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        #region ICommands

        public ICommand ExitCommand => _exitCommand;

        public ICommand ResetCommand => _resetCommand;

        public ICommand SaveCommand => _saveCommand;

        public ICommand OpenCommand => _openCommand;

        public ICommand BrowseCommand => _browseCommand;

        public ICommand SelectedImageCommand => _selectedImageCommand;

        public ICommand RunCommand => _runCommand;

        #endregion

        private string _iaiOutputMessage;
        private string _selectedImagePath;
        private int _sliderBrightness;
        private int _sliderContrast;
        private int _sliderSaturation;
        private MemoryStream _modifiedImageStream;
        private bool hasUserModifiedImage;

        /// <summary>
        /// Getter/Setter for the python output string for troubleshooting
        /// </summary>
        public string PythonOutput
        {
            get => _pythonOutput;
            set
            {
                _pythonOutput = value;
                OnPropertyChanged(nameof(PythonOutput));
            }
        }

        /// <summary>
        /// Getter/Setter for the WeakAreaCamoDescription
        /// </summary>
        public string WeakAreaCamoDescription
        {
            get { return _weakAreaCamoDescription; }
            set
            {
                _weakAreaCamoDescription = value;
                OnPropertyChanged(nameof(WeakAreaCamoDescription));
            }
        }

        /// <summary>
        /// Getter/Setter for the IAI Output Message
        /// </summary>
        public string IAIOutputMessage
        {
            get { return _iaiOutputMessage; }
            set
            {
                _iaiOutputMessage = value;
                OnPropertyChanged(nameof(IAIOutputMessage));
            }
        }

        /// <summary>
        /// Getter/Setter for the user selected image path.
        /// </summary>
        public string SelectedImagePath
        {
            get { return _selectedImagePath; }
            set
            {
                _selectedImagePath = value;
                OnPropertyChanged(nameof(SelectedImagePath));
                UpdateSelectedImageFileName(); // Update SelectedImageFileName when SelectedImagePath changes

                // Enable or disable the Run button based on whether an image is selected
                IsRunButtonEnabled = !string.IsNullOrEmpty(value);
            }
        }

        /// <summary>
        /// Getter/Setter for the user selected image file name.
        /// </summary>
        public string SelectedImageFileName
        {
            get => _selectedImageFileName;
            private set
            {
                _selectedImageFileName = value;
                OnPropertyChanged(nameof(SelectedImageFileName));
            }
        }

        /// <summary>
        /// Getter/Setter for the user selected image file name without the file extension.
        /// </summary>
        public string SelectedImageName
        {
            get => _selectedImageName;
            private set
            {
                _selectedImageName = value;
                OnPropertyChanged(nameof(SelectedImageName));
            }
        }

        /// <summary>
        /// Returns the user selected image to be displayed on the GUI.
        /// </summary>
        public BitmapImage SelectedImage
        {
            get { return _selectedImage; }
            set
            {
                _selectedImage = value;
                OnPropertyChanged(nameof(SelectedImage));
            }
        }

        public BitmapImage FACEPredictionImage
        {
            get { return _facePredictionImage; }
            set
            {
                _facePredictionImage = value;
                OnPropertyChanged(nameof(FACEPredictionImage));
            }
        }

        public BitmapImage WeakAreaCamoImage
        {
            get { return _weakAreaCamoImage;  }
            set 
            {
                _weakAreaCamoImage = value;
                OnPropertyChanged(nameof(WeakAreaCamoImage));
            }
        }

        /// <summary>
        /// Returns the preview/mouse-over image to be displayed on the GUI.
        /// </summary>
        public BitmapImage PreviewImage
        {
            get { return _previewImage; }
            set
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        /// <summary>
        /// Getter/Setter for RankNetFixationDecoderImage
        /// </summary>
        public BitmapImage RankNetFixationDecoderImage
        {
            get { return _ranknetFixationImage; }
            set
            {
                _ranknetFixationImage = value;
                OnPropertyChanged(nameof(RankNetFixationDecoderImage));
            }
        }

        /// <summary>
        /// Getter/Setter for RankNetCamouflageDecoderImage
        /// </summary>
        public BitmapImage RankNetCamouflageDecoderImage
        {
            get { return _ranknetCamouflageImage; }
            set
            {
                _ranknetCamouflageImage = value;
                OnPropertyChanged(nameof(RankNetCamouflageDecoderImage));
            }
        }

        public BitmapImage NoSelectedImage
        {
            get { return new BitmapImage(new Uri("pack://application:,,,/MURDOC;component/Assets/image_placeholder.png")); ; }
            set { }
        }

        public int SliderBrightness
        {
            get
            {
                return _sliderBrightness;
            }
            set
            {
                _sliderBrightness = value;
                UpdateSelectedImage(_sliderBrightness, _sliderContrast, _sliderSaturation);
            }
        }

        public int SliderContrast
        {
            get
            {
                return _sliderContrast;
            }
            set
            {
                _sliderContrast = value;
                UpdateSelectedImage(_sliderBrightness, _sliderContrast, _sliderSaturation);
            }
        }

        public int SliderSaturation
        {
            get
            {
                return _sliderSaturation;
            }
            set
            {
                _sliderSaturation = value;
                UpdateSelectedImage(_sliderBrightness, _sliderContrast, _sliderSaturation);
            }
        }

        #region ResNet50 Off-Ramp Images
        /// <summary>
        /// Setter will set the appropriate off-ramp images dependant on:
        ///         (1) if the model has run
        ///         (2) if the off-ramp image exists in the appropriate folder
        /// </summary>
        private string _resNet50ConvImagePath;
        public string ResNet50ConvImagePath
        {
            get { return _resNet50ConvImagePath; }
            set
            {
                if (_resNet50ConvImagePath != value)
                {
                    _resNet50ConvImagePath = value;
                    OnPropertyChanged(nameof(ResNet50ConvImagePath));

                    // Load and set the image directly to ResNet50Conv
                    LoadResNet50ConvImage();
                }
            }
        }

        private BitmapImage _resNet50ConvImage;
        public BitmapImage ResNet50Conv
        {
            get { return _resNet50ConvImage; }
            set
            {
                _resNet50ConvImage = value;
                OnPropertyChanged(nameof(ResNet50Conv));
            }
        }

        #region RankNet X1
        private BitmapImage _resNet50Layer1;
        public BitmapImage RankNetX1Image
        {
            get => _resNet50Layer1;
            set
            {
                if (_resNet50Layer1 != value)
                {
                    _resNet50Layer1 = value;
                    OnPropertyChanged(nameof(RankNetX1Image));
                }
            }
        }

        private string _resNet50Layer1ImagePath;
        public string RankNetX1ImagePath
        {
            get => _resNet50Layer1ImagePath;
            set
            {
                if (_resNet50Layer1ImagePath != value)
                {
                    _resNet50Layer1ImagePath = value;
                    OnPropertyChanged(nameof(RankNetX1ImagePath));

                    // Load and set the image directly to RankNetX1Image
                    LoadRankNetX1Image();
                }
            }
        }
        #endregion

        #region RankNet X2
        private BitmapImage _resNet50Layer2;
        public BitmapImage RankNetX2Image
        {
            get => _resNet50Layer2;
            set
            {
                if (_resNet50Layer2 != value)
                {
                    _resNet50Layer2 = value;
                    OnPropertyChanged(nameof(RankNetX2Image));
                }
            }
        }

        private string _resNet50Layer2ImagePath;
        public string RankNetX2ImagePath
        {
            get => _resNet50Layer2ImagePath;
            set
            {
                if (_resNet50Layer2ImagePath != value)
                {
                    _resNet50Layer2ImagePath = value;
                    OnPropertyChanged(nameof(RankNetX2ImagePath));

                    // Load and set the image directly to RankNetX2Image
                    LoadRankNetX2Image();
                }
            }
        }
        #endregion

        #region RankNet X3
        private BitmapImage _resNet50Layer3;
        public BitmapImage RankNetX3Image
        {
            get => _resNet50Layer3;
            set
            {
                if (_resNet50Layer3 != value)
                {
                    _resNet50Layer3 = value;
                    OnPropertyChanged(nameof(RankNetX3Image));
                }
            }
        }

        private string _resNet50Layer3ImagePath;
        public string RankNetX3ImagePath
        {
            get => _resNet50Layer3ImagePath;
            set
            {
                if (_resNet50Layer3ImagePath != value)
                {
                    _resNet50Layer3ImagePath = value;
                    OnPropertyChanged(nameof(RankNetX3ImagePath));

                    // Load and set the image directly to RankNetX3Image
                    LoadRankNetX3Image();
                }
            }
        }
        #endregion

        #region RankNet X4
        private BitmapImage _resNet50Layer4;
        public BitmapImage RankNetX4Image
        {
            get => _resNet50Layer4;
            set
            {
                if (_resNet50Layer4 != value)
                {
                    _resNet50Layer4 = value;
                    OnPropertyChanged(nameof(RankNetX4Image));
                }
            }
        }

        private string _resNet50Layer4ImagePath;
        public string RankNetX4ImagePath
        {
            get => _resNet50Layer4ImagePath;
            set
            {
                if (_resNet50Layer4ImagePath != value)
                {
                    _resNet50Layer4ImagePath = value;
                    OnPropertyChanged(nameof(RankNetX4ImagePath));

                    // Load and set the image directly to RankNetX4Image
                    LoadRankNetX4Image();
                }
            }
        }
        #endregion
                     
        #region RankNet X2_2
        private BitmapImage _resNet50Layer1_2;
        public BitmapImage RankNetX2_2Image
        {
            get => _resNet50Layer1_2;
            set
            {
                if (_resNet50Layer1_2 != value)
                {
                    _resNet50Layer1_2 = value;
                    OnPropertyChanged(nameof(RankNetX2_2Image));
                }
            }
        }

        private string _resNet50Layer1_2ImagePath;
        public string RankNetX2_2ImagePath
        {
            get => _resNet50Layer1_2ImagePath;
            set
            {
                if (_resNet50Layer1_2ImagePath != value)
                {
                    _resNet50Layer1_2ImagePath = value;
                    OnPropertyChanged(nameof(RankNetX2_2ImagePath));

                    // Load and set the image directly to RankNetX2_2Image
                    LoadRankNetX2_2Image();
                }
            }
        }
        #endregion

        #region RankNet X3_2
        private BitmapImage _resNet50Layer3_2;
        public BitmapImage RankNetX3_2Image
        {
            get => _resNet50Layer3_2;
            set
            {
                if (_resNet50Layer3_2 != value)
                {
                    _resNet50Layer3_2 = value;
                    OnPropertyChanged(nameof(RankNetX3_2Image));
                }
            }
        }

        private string _resNet50Layer3_2ImagePath;
        public string RankNetX3_2ImagePath
        {
            get => _resNet50Layer3_2ImagePath;
            set
            {
                if (_resNet50Layer3_2ImagePath != value)
                {
                    _resNet50Layer3_2ImagePath = value;
                    OnPropertyChanged(nameof(RankNetX3_2ImagePath));

                    // Load and set the image directly to RankNetX2Image
                    LoadRankNetX3_2Image();
                }
            }
        }
        #endregion

        #region RankNet X4_2
        private BitmapImage _resNet50Layer4_2;
        public BitmapImage RankNetX4_2Image
        {
            get => _resNet50Layer4_2;
            set
            {
                if (_resNet50Layer4_2 != value)
                {
                    _resNet50Layer4_2 = value;
                    OnPropertyChanged(nameof(RankNetX4_2Image));
                }
            }
        }

        private string _resNet50Layer4_2ImagePath;
        public string RankNetX4_2ImagePath
        {
            get => _resNet50Layer4_2ImagePath;
            set
            {
                if (_resNet50Layer4_2ImagePath != value)
                {
                    _resNet50Layer4_2ImagePath = value;
                    OnPropertyChanged(nameof(RankNetX4_2ImagePath));

                    // Load and set the image directly to RankNetX3Image
                    LoadRankNetX4_2Image();
                }
            }
        }
        #endregion

        #region RankNet Ref_Pred
        private BitmapImage _resNet50LayerRef_Pred;
        public BitmapImage RankNetRef_PredImage
        {
            get => _resNet50LayerRef_Pred;
            set
            {
                if (_resNet50LayerRef_Pred != value)
                {
                    _resNet50LayerRef_Pred = value;
                    OnPropertyChanged(nameof(RankNetRef_PredImage));
                }
            }
        }

        private string _resNet50LayerRef_PredImagePath;
        public string RankNetRef_PredImagePath
        {
            get => _resNet50LayerRef_PredImagePath;
            set
            {
                if (_resNet50LayerRef_PredImagePath != value)
                {
                    _resNet50LayerRef_PredImagePath = value;
                    OnPropertyChanged(nameof(RankNetRef_PredImagePath));

                    // Load and set the image directly to RankNetX4Image
                    LoadRankNetRef_PredImage();
                }
            }
        }
        #endregion

        private BitmapImage _resNet50Output;
        public BitmapImage ResNet50Output
        {
            get => _resNet50Output;
            set
            {
                if (_resNet50Output != value)
                {
                    _resNet50Output = value;
                    OnPropertyChanged(nameof(ResNet50Output));
                }
            }
        }

        private string _resNet50OutputImagePath;
        public string ResNet50OutputImagePath
        {
            get => _resNet50OutputImagePath;
            set
            {
                if (_resNet50OutputImagePath != value)
                {
                    _resNet50OutputImagePath = value;
                    OnPropertyChanged(nameof(ResNet50OutputImagePath));

                    // Load and set the image directly to RankNetX4Image
                    LoadRankNetOutputImage();
                }
            }
        }

        private string _rankNetFixationDecoderImagePath;
        public string RankNetFixationDecoderImagePath
        {
            get => _rankNetFixationDecoderImagePath;
            set
            {
                if (_rankNetFixationDecoderImagePath != value)
                {
                    _rankNetFixationDecoderImagePath = value;
                    OnPropertyChanged(nameof(RankNetFixationDecoderImagePath));
                }
            }
        }

        private string _rankNetCamouflageDecoderImagePath;
        public string RankNetCamouflageDecoderImagePath
        {
            get => _rankNetCamouflageDecoderImagePath;
            set
            {
                if (_rankNetCamouflageDecoderImagePath != value)
                {
                    _rankNetCamouflageDecoderImagePath = value;
                    OnPropertyChanged(nameof(RankNetCamouflageDecoderImagePath));
                }
            }
        }

        private string _facePredictionImagePath;
        public string FACEPredictionImagePath
        {
            get => _facePredictionImagePath;
            set
            {
                if (_facePredictionImagePath != value)
                {
                    _facePredictionImagePath = value;
                    OnPropertyChanged(nameof(FACEPredictionImagePath));
                }
            }
        }

        private string _weakAreaCamoImagePath;
        public string WeakAreaCamoImagePath
        {
            get => _weakAreaCamoImagePath;
            set
            {
                if (_weakAreaCamoImagePath != value)
                {
                    _weakAreaCamoImagePath = value;
                    OnPropertyChanged(nameof(WeakAreaCamoImagePath));
                }
            }
        }
        #endregion

        /// <summary>
        /// Disables/Enables the Run button 
        /// Relies upon if a user selects an image or not
        /// </summary>
        private bool _isRunButtonEnabled;
        public bool IsRunButtonEnabled
        {
            get { return _isRunButtonEnabled; }
            set
            {
                _isRunButtonEnabled = value;
                OnPropertyChanged(nameof(IsRunButtonEnabled));
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public MainWindowViewModel()
        {
            InitializePythonRuntime();

            IsRunButtonEnabled = false; // Disable Run button initially

            _modifiedImageStream = new MemoryStream();

            // Set the default placeholder image
            SelectedImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            PreviewImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));

            // Create Temporary Folder Location
            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp"));

            // Selected Image
            LoadImage();

            // RankNet Images
            LoadResNet50ConvImage();
            LoadRankNetX1Image();
            LoadRankNetX2Image();
            LoadRankNetX3Image();
            LoadRankNetX4Image();
            LoadRankNetFixationImage();

            LoadRankNetX2_2Image();
            LoadRankNetX3_2Image();
            LoadRankNetX4_2Image();
            LoadRankNetRef_PredImage();
            LoadRankNetCamouflageImage();

            // FACE Prediction Image
            LoadRankNetOutputImage();

            _exitCommand = new RelayCommand(ExecuteExitCommand);

            _browseCommand = new RelayCommand(ExecuteBrowseCommand);
            _selectedImageCommand = new RelayCommand(LoadImage);

            _runCommand = new RelayCommand(ExecuteRunCommand);
            _resetCommand = new RelayCommand(ExecuteResetCommand);
        }

        /// <summary>
        /// Closes the application.
        /// </summary>
        private void ExecuteExitCommand()
        {
            Console.WriteLine("In ExecuteExitCommand()");

            // Add logic to exit the application
            Environment.Exit(0);
        }

        /// <summary>
        /// 
        /// </summary>
        private void ExecuteResetCommand()
        {
            string executableDir = AppDomain.CurrentDomain.BaseDirectory;
            string offrampsFolderPath = Path.Combine(executableDir, "offramp_output_images");

            ImageCleaner.ClearImages(offrampsFolderPath);

            // Logic for reset command - reset everything on the screen
            PreviewImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));

            ResNet50Conv = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetX1Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetX2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetX3Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetX4Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetX2_2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetX3_2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetX4_2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetRef_PredImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            ResNet50Output = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetFixationDecoderImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            RankNetCamouflageDecoderImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            WeakAreaCamoImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            FACEPredictionImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
        }

        /// <summary>
        /// 
        /// </summary>
        private void ExecuteOpenCommand()
        {
            // TODO: Add logic for open command
        }

        /// <summary>
        /// 
        /// </summary>
        private void ExecuteSaveCommand()
        {
            // TODO: Add logic for save command - Save the models 'visualization' as a PDF
        }

        /// <summary>
        /// Executes the BrowseCommand to open a file dialog for selecting an image file.
        /// </summary>
        private void ExecuteBrowseCommand()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp";
            if (openFileDialog.ShowDialog() == true)
            {
                hasUserModifiedImage = false;
                SelectedImagePath = openFileDialog.FileName;
                LoadImage();
            }
        }

        /// <summary>
        /// Run the FACE model (submodels include ResNet50, RankNet, and EfficientDet-D7)
        /// </summary>
        private void ExecuteRunCommand()
        {
            // Need to handle the scenario by preventing the run when SelectedImagePath has no image selected - done: disabled the run models button if no image is selected
            try
            {
                // Change cursor to wait cursor
                Mouse.OverrideCursor = Cursors.Wait;
                PythonOutput = "Initializing Python environment...\n";

                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    dynamic os = Py.Import("os");

                    // Add the directory containing your Python script to Python's sys.path
                    string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\", "Model", "IAI_Decision_Hierarchy.py");
                    sys.path.append(os.path.dirname(scriptPath));

                    // ResNet50
                    try
                    {
                        // Import your Python script module
                        dynamic script = Py.Import("IAI_Decision_Hierarchy");
                        Console.WriteLine("IAI_Decision_Hierarchy imported successfully");
                        PythonOutput += "Script imported successfully. Redirecting stdout...\n";

                        // Save Temporary Image if user modified
                        string tempImageLocation = SaveTemporaryImage();
                        string imageLocation = string.IsNullOrEmpty(tempImageLocation) ? SelectedImagePath : tempImageLocation;

                        PythonOutput += "Calling iaiDecision function...\n";
                        // Call the iaiDecision_test function from your Python script
                        string message = script.iaiDecision(imageLocation); //IAIOutputMessage

                        // Update UI on the main thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            string executableDir = AppDomain.CurrentDomain.BaseDirectory;
                            string offrampsFolderPath = Path.Combine(executableDir, "offramp_output_images");
                            string outputsFolderPath = Path.Combine(executableDir, "outputs", _selectedImageName);
                            string detectionFolderPath = Path.Combine(executableDir, "detection_results");
                            string folderPath = Path.Combine(executableDir, "results");

                            // RankNet ==========================================   
                            string layer1ImagePath = Path.Combine(offrampsFolderPath, "x1.png");
                            Console.WriteLine(layer1ImagePath);
                            RankNetX1ImagePath = layer1ImagePath;
                            OnPropertyChanged(nameof(RankNetX1Image));
                                                        
                            string layer2ImagePath = Path.Combine(offrampsFolderPath, "x2.png");
                            Console.WriteLine(layer2ImagePath);
                            RankNetX2ImagePath = layer2ImagePath;
                            OnPropertyChanged(nameof(RankNetX2ImagePath));
                                                        
                            string layer3ImagePath = Path.Combine(offrampsFolderPath, "x3.png");
                            Console.WriteLine(layer3ImagePath);
                            RankNetX3ImagePath = layer3ImagePath;
                            OnPropertyChanged(nameof(RankNetX3Image));
                                                        
                            string layer4ImagePath = Path.Combine(offrampsFolderPath, "x4.png");
                            Console.WriteLine(layer4ImagePath);
                            RankNetX4ImagePath = layer4ImagePath;
                            OnPropertyChanged(nameof(RankNetX4Image));

                            string fixationDecoderImagePath = Path.Combine(outputsFolderPath, "binary_image.png");
                            RankNetFixationDecoderImagePath = fixationDecoderImagePath;
                            OnPropertyChanged(nameof(RankNetFixationDecoderImage));
                            LoadRankNetFixationImage();

                            // ==========================================                                                       
                            string layer2_2ImagePath = Path.Combine(offrampsFolderPath, "x2_2.png");
                            RankNetX2_2ImagePath = layer2_2ImagePath;
                            OnPropertyChanged(nameof(RankNetX2_2Image));

                            string layer3_2ImagePath = Path.Combine(offrampsFolderPath, "x3_2.png");
                            RankNetX3_2ImagePath = layer3_2ImagePath;
                            OnPropertyChanged(nameof(RankNetX3_2Image));

                            string layer4_2ImagePath = Path.Combine(offrampsFolderPath, "x4_2.png");
                            RankNetX4_2ImagePath = layer4_2ImagePath;
                            OnPropertyChanged(nameof(RankNetX4_2Image));

                            string layerRef_PredImagePath = Path.Combine(offrampsFolderPath, "ref_pred.png");
                            RankNetRef_PredImagePath = layerRef_PredImagePath;
                            OnPropertyChanged(nameof(RankNetRef_PredImage));

                            string camouflageDecoderImagePath = Path.Combine(outputsFolderPath, "fixation_image.png");
                            RankNetCamouflageDecoderImagePath = camouflageDecoderImagePath;
                            OnPropertyChanged(nameof(RankNetCamouflageDecoderImagePath));
                            LoadRankNetCamouflageImage();

                            // EfficientDet-D7 output ==========================================   
                            string weakAreaCamoImagePath = Path.Combine(detectionFolderPath, _selectedImageName + ".png");
                            WeakAreaCamoImagePath = weakAreaCamoImagePath;
                            OnPropertyChanged(nameof(WeakAreaCamoImagePath));
                            LoadDetectionImage();

                            string weakAreaCamoTextPath = Path.Combine(detectionFolderPath, _selectedImageName + ".txt");
                            OpenAndReadFile(weakAreaCamoTextPath);


                            // FACE Prediction Output ==========================================   
                            string facePredictionImagePath = Path.Combine(folderPath, "segmented_" + _selectedImageName + ".jpg");
                            FACEPredictionImagePath = facePredictionImagePath;
                            OnPropertyChanged(nameof(FACEPredictionImagePath));
                            LoadPredictionImage();

                            IAIOutputMessage = message;
                            OnPropertyChanged(nameof(IAIOutputMessage));
                        });
                    }
                    catch (PythonException exception)
                    {
                        // Probably should indicate somewhere on the GUI that something went wrong
                        Console.WriteLine("Exception occured: " + exception);
                    }
                }   
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General Exception: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }
            finally
            {
                // Reset cursor back to default
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Initializes Python Engine with the default Visual Studio Python DLL location
        /// </summary>
        private void InitializePythonRuntime()
        {
            try
            {
                Console.WriteLine("Starting Python runtime initialization...");

                string pythonHome = @"C:\Users\pharm\AppData\Local\Programs\Python\Python39";
                string pythonDll = Environment.GetEnvironmentVariable("PythonDLL", EnvironmentVariableTarget.User);

                Console.WriteLine($"Python Home: {pythonHome}");
                Console.WriteLine($"Python DLL: {pythonDll}");

                if (string.IsNullOrEmpty(pythonDll))
                {
                    throw new Exception("PythonDLL environment variable is not set.");
                }

                Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDll);
                Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("PYTHONPATH", $"{pythonHome}\\Lib\\site-packages;{pythonHome}\\Lib", EnvironmentVariableTarget.Process);

                Console.WriteLine("Environment variables set. Attempting to initialize Python Engine...");

                if (!PythonEngine.IsInitialized)
                {
                    PythonEngine.Initialize();
                    Console.WriteLine("Python runtime initialized successfully.");
                }
                else
                {
                    Console.WriteLine("Python runtime was already initialized.");
                }

                // Test Python functionality
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    Console.WriteLine($"Python version: {sys.version}");
                    Console.WriteLine($"Python path: {string.Join(", ", sys.path)}");
                }
            }
            catch (TypeInitializationException tiex)
            {
                // Log or display the type initialization exception message and inner exception
                Console.WriteLine("Type Initialization Exception: " + tiex.Message);
                Console.WriteLine("Inner Exception: " + tiex.InnerException?.Message);
            }
            catch (Exception ex)
            {
                // Log or display any other exceptions during initialization
                Console.WriteLine("Error initializing Python engine: " + ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Function to open a text file and display the contents in the View's TextBlock
        /// </summary>
        /// <param name="filePath"></param>
        public void OpenAndReadFile(string filePath)
        {
            try
            {
                WeakAreaCamoDescription = File.ReadAllText(filePath);

                if (WeakAreaCamoDescription.Equals(String.Empty))
                {
                    WeakAreaCamoDescription = "EfficientDet-D7 could not identify weak camouflaged object parts.";
                }
            }
            catch (Exception ex)
            {
                WeakAreaCamoDescription = $"Error reading file: {ex.Message}";
            }
            finally
            {
                OnPropertyChanged(nameof(WeakAreaCamoDescription));
            }
        }

        #region Load Images Functions
        /// <summary>
        /// Refreshes the RankNet Binary Map Image on the GUI
        /// </summary>
        private void LoadRankNetFixationImage()
        {
            if (!string.IsNullOrEmpty(RankNetFixationDecoderImagePath))
            {
                RankNetFixationDecoderImage = new BitmapImage(new Uri(RankNetFixationDecoderImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetFixationDecoderImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Refreshes the Resnet Layer 2_2 Image in the GUI
        /// </summary>
        private void LoadRankNetX2_2Image()
        {
            if (!string.IsNullOrEmpty(RankNetX2_2ImagePath))
            {
                RankNetX2_2Image = new BitmapImage(new Uri(RankNetX2_2ImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetX2_2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Refreshes the Resnet Layer 3_2 Image in the GUI
        /// </summary>
        private void LoadRankNetX3_2Image()
        {
            if (!string.IsNullOrEmpty(RankNetX3_2ImagePath))
            {
                RankNetX3_2Image = new BitmapImage(new Uri(RankNetX3_2ImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetX3_2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Refreshes the Resnet Layer 4_2 Image in the GUI
        /// </summary>
        private void LoadRankNetX4_2Image()
        {
            if (!string.IsNullOrEmpty(RankNetX4_2ImagePath))
            {
                RankNetX4_2Image = new BitmapImage(new Uri(RankNetX4_2ImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetX4_2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        private void LoadRankNetRef_PredImage()
        {
            if (!string.IsNullOrEmpty(RankNetRef_PredImagePath))
            {
                RankNetRef_PredImage = new BitmapImage(new Uri(RankNetRef_PredImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetRef_PredImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Refreshes the RankNet Fixation Map Image on the GUI
        /// </summary>
        private void LoadRankNetCamouflageImage()
        {
            if (!string.IsNullOrEmpty(RankNetCamouflageDecoderImagePath))
            {
                RankNetCamouflageDecoderImage = new BitmapImage(new Uri(RankNetCamouflageDecoderImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetCamouflageDecoderImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Refreshes the Prediction Image on the GUI
        /// </summary>
        private void LoadPredictionImage()
        {
            if (!string.IsNullOrEmpty(FACEPredictionImagePath))
            {
                FACEPredictionImage = new BitmapImage(new Uri(FACEPredictionImagePath));
            }
            else
            {
                // Set the default placeholder image
                FACEPredictionImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Refreshes the Prediction Image on the GUI
        /// </summary>
        private void LoadDetectionImage()
        {
            if (!string.IsNullOrEmpty(WeakAreaCamoImagePath))
            {
                WeakAreaCamoImage = new BitmapImage(new Uri(WeakAreaCamoImagePath));
            }
            else
            {
                // Set the default placeholder image
                WeakAreaCamoImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadImage()
        {
            if (hasUserModifiedImage)
            {
                BitmapImage tempModifiedImage = new BitmapImage();
                tempModifiedImage.BeginInit();
                tempModifiedImage.CacheOption = BitmapCacheOption.OnLoad;
                tempModifiedImage.StreamSource = _modifiedImageStream;
                tempModifiedImage.EndInit();
                SelectedImage = tempModifiedImage;
            }
            else if (!string.IsNullOrEmpty(SelectedImagePath))
            {
                SelectedImage = new BitmapImage(new Uri(SelectedImagePath));
            }
            else
            {
                // Set the default placeholder image
                SelectedImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }
        #endregion

        /// <summary>
        /// Updates the selected image with user defined brightnesss, contrast, and saturation values
        /// </summary>
        /// <param name="brightness">User Defined Brightness</param>
        /// <param name="contrast">User Defined Contrast</param>
        /// <param name="saturation">User Defined Saturation</param>
        private void UpdateSelectedImage(int brightness, int contrast, int saturation)
        {
            if (!string.IsNullOrEmpty(SelectedImagePath))
            {
                byte[] imageBytes = File.ReadAllBytes(SelectedImagePath);
                using (MemoryStream inStream = new MemoryStream(imageBytes))
                {
                    using (ImageFactory imageFactory = new ImageFactory(preserveExifData: true))
                    {
                        hasUserModifiedImage = true;
                        imageFactory.Load(inStream);
                        imageFactory.Brightness(brightness);
                        imageFactory.Contrast(contrast);
                        imageFactory.Saturation(saturation);
                        imageFactory.Save(_modifiedImageStream);
                        LoadImage();
                    }
                }
            }
        }

        /// <summary>
        /// Saves the temporary image to a temp folder in JPG format
        /// </summary>
        /// <returns>String of location saved</returns>
        private string SaveTemporaryImage()
        {
            string tempPath = string.Empty;
            if (hasUserModifiedImage)
            {
                byte[] imageBytes = File.ReadAllBytes(SelectedImagePath);
                using (MemoryStream inStream = new MemoryStream(imageBytes))
                {
                    using (ImageFactory imageFactory = new ImageFactory(preserveExifData: true))
                    {
                        tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", SelectedImageName + ".jpg");
                        imageFactory.Load(inStream);
                        imageFactory.Brightness(_sliderBrightness);
                        imageFactory.Contrast(_sliderContrast);
                        imageFactory.Saturation(_sliderSaturation);
                        // Delete old temp file if exists since it won't overwrite
                        File.Delete(tempPath);
                        imageFactory.Save(tempPath);
                    }
                }
            }
            return tempPath;
        }

        

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadResNet50ConvImage()
        {
            if (!string.IsNullOrEmpty(ResNet50ConvImagePath))
            {
                ResNet50Conv = new BitmapImage(new Uri(ResNet50ConvImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Conv = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadRankNetX1Image()
        {
            if (!string.IsNullOrEmpty(RankNetX1ImagePath))
            {
                Console.WriteLine("Image exists.");
                RankNetX1Image = new BitmapImage(new Uri(RankNetX1ImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetX1Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadRankNetX2Image()
        {
            if (!string.IsNullOrEmpty(RankNetX2ImagePath))
            {
                RankNetX2Image = new BitmapImage(new Uri(RankNetX2ImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetX2Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadRankNetX3Image()
        {
            if (!string.IsNullOrEmpty(RankNetX3ImagePath))
            {
                RankNetX3Image = new BitmapImage(new Uri(RankNetX3ImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetX3Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadRankNetX4Image()
        {
            if (!string.IsNullOrEmpty(RankNetX4ImagePath))
            {
                RankNetX4Image = new BitmapImage(new Uri(RankNetX4ImagePath));
            }
            else
            {
                // Set the default placeholder image
                RankNetX4Image = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadRankNetOutputImage()
        {
            if (!string.IsNullOrEmpty(ResNet50OutputImagePath))
            {
                ResNet50Output = new BitmapImage(new Uri(ResNet50OutputImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Output = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Updates the GUI to display the user selected image file name
        /// </summary>
        private void UpdateSelectedImageFileName()
        {
            if (SelectedImagePath != "Assets/image_placeholder.png")
            {
                SelectedImageFileName = Path.GetFileName(SelectedImagePath);
                SelectedImageName = Path.GetFileNameWithoutExtension(SelectedImagePath);
            }
        }

        /// <summary>
        /// Handles the changing of the preview image upon mouse over of a process image.
        /// </summary>
        /// <param name="imagePath">The image that is moused over.</param>
        /// <remarks>
        /// </remarks>
        public void HandlePreviewImageChanged(string imagePath)
        {
            PreviewImage = new BitmapImage(new Uri(imagePath));
        }

        /// <summary>
        /// Invokes the PropertyChanged event to notify subscribers of a property change.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}