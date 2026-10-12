// HA Video Reference: a reference video next to Roblox Studio that follows Heaven's Animator's timeline.
// One small Windows app, nothing to install: a WPF window (.NET Framework 4.x, part of every Windows 10 / 11), Windows'
// own video decoder (Media Foundation: the exact frame, like Blender's video strip) and a local server on
// http://localhost:PORT/ that the "HA Video Sync" Studio plugin posts the frame to. Only listens on localhost: nothing
// outside this computer can reach it.
//   POST /sync    {frame, fps, playing, speed}              (from the plugin)
//   GET  /state   {frame, fps, playing, speed, seq, age}    (age = seconds since Studio last synced; -1 = never)
//   GET  /ping    {ok, app, version}
// "HA Video Reference.exe" 34901 starts it on another port (default 34900); a video's path (or a video dropped on the
// .exe) opens that video.
// The sidebar follows the owner's Blender plugin: Start (the Studio frame where the video starts; Earlier / Later,
// Start Here), Trim (video frames skipped at its start), Speed, Loop, Mute, Volume; middle mouse pans, the wheel zooms,
// Home fits. Built by companion/build_video_reference.py with the C# 5 compiler that ships with Windows (csc.exe).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

[assembly: AssemblyTitle("HA Video Reference")]
[assembly: AssemblyProduct("HA Video Reference")]
[assembly: AssemblyDescription("A reference video that follows Heaven's Animator's timeline in Roblox Studio")]
[assembly: AssemblyCompany("MrHxaven")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

static class Program
{
	[STAThread]
	static int Main(string[] args)
	{
		int port = 34900;
		string video = null;
		foreach (string a in args)
		{
			int p;
			if (int.TryParse(a, out p) && p > 0 && p < 65536) port = p;
			else if (File.Exists(a)) video = System.IO.Path.GetFullPath(a);
		}
		var app = new Application();
		return app.Run(new Player(port, video).Window);
	}
}

class Player
{
	const int VERSION = 2;
	static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
	static readonly string SettingsPath = System.IO.Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HA Video Reference", "settings.txt");

	const string XAML = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
		xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
		Title='HA Video Reference' Width='1100' Height='680' MinWidth='640' MinHeight='420'
		Background='{DynamicResource bg}' Foreground='{DynamicResource text}' FontFamily='Segoe UI' FontSize='14' AllowDrop='True'
		UseLayoutRounding='True' WindowStartupLocation='CenterScreen'>
	<Window.Resources>
		<!-- Heaven's Animator's theme tokens (Graphite until Studio sends the current theme) -->
		<SolidColorBrush x:Key='bg' Color='#1C1D20'/>
		<SolidColorBrush x:Key='panel' Color='#232428'/>
		<SolidColorBrush x:Key='panelAlt' Color='#2A2B30'/>
		<SolidColorBrush x:Key='border' Color='#36383E'/>
		<SolidColorBrush x:Key='text' Color='#F2F3F5'/>
		<SolidColorBrush x:Key='textDim' Color='#B5B8BF'/>
		<SolidColorBrush x:Key='textMuted' Color='#7D808A'/>
		<SolidColorBrush x:Key='accent' Color='#FFB02E'/>
		<SolidColorBrush x:Key='accentText' Color='#1D1300'/>
		<SolidColorBrush x:Key='accent2' Color='#F59E0B'/>
		<SolidColorBrush x:Key='select' Color='#4A3A1C'/>
		<SolidColorBrush x:Key='button' Color='#2C2D32'/>
		<SolidColorBrush x:Key='buttonStroke' Color='#3E4148'/>
		<SolidColorBrush x:Key='input' Color='#1A1B1E'/>
		<SolidColorBrush x:Key='success' Color='#4ADE80'/>
		<SolidColorBrush x:Key='warning' Color='#FBBF24'/>
		<SolidColorBrush x:Key='danger' Color='#FF5C5C'/>
		<ControlTemplate x:Key='ButtonT' TargetType='ButtonBase'>
			<Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
					BorderThickness='1' CornerRadius='4' Padding='{TemplateBinding Padding}'>
				<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
			</Border>
			<ControlTemplate.Triggers>
				<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource accent}'/></Trigger>
			</ControlTemplate.Triggers>
		</ControlTemplate>
		<Style x:Key='Base' TargetType='ButtonBase'>
			<Setter Property='Template' Value='{StaticResource ButtonT}'/>
			<Setter Property='Background' Value='{DynamicResource button}'/>
			<Setter Property='BorderBrush' Value='{DynamicResource buttonStroke}'/>
			<Setter Property='Foreground' Value='{DynamicResource text}'/>
			<Setter Property='Padding' Value='8,6'/>
			<Setter Property='Cursor' Value='Hand'/>
		</Style>
		<Style TargetType='Button' BasedOn='{StaticResource Base}'/>
		<Style x:Key='Small' TargetType='Button' BasedOn='{StaticResource Base}'>
			<Setter Property='Padding' Value='0,4'/>
			<Setter Property='Margin' Value='2,0'/>
		</Style>
		<Style TargetType='ToggleButton' BasedOn='{StaticResource Base}'>
			<Style.Triggers>
				<Trigger Property='IsChecked' Value='True'>
					<Setter Property='Background' Value='{DynamicResource select}'/>
					<Setter Property='BorderBrush' Value='{DynamicResource accent}'/>
					<Setter Property='Foreground' Value='{DynamicResource accent}'/>
				</Trigger>
			</Style.Triggers>
		</Style>
		<Style TargetType='TextBox'>
			<Setter Property='Width' Value='90'/>
			<Setter Property='Padding' Value='5,3'/>
			<Setter Property='Background' Value='{DynamicResource input}'/>
			<Setter Property='Foreground' Value='{DynamicResource text}'/>
			<Setter Property='BorderBrush' Value='{DynamicResource border}'/>
			<Setter Property='CaretBrush' Value='{DynamicResource text}'/>
			<Setter Property='HorizontalContentAlignment' Value='Right'/>
			<Setter Property='VerticalContentAlignment' Value='Center'/>
		</Style>
		<Style x:Key='Row' TargetType='DockPanel'><Setter Property='Margin' Value='0,0,0,8'/></Style>
		<Style x:Key='Rule' TargetType='Rectangle'>
			<Setter Property='Height' Value='1'/><Setter Property='Fill' Value='{DynamicResource border}'/><Setter Property='Margin' Value='0,4,0,12'/>
		</Style>
	</Window.Resources>
	<DockPanel>
		<Border DockPanel.Dock='Top' Background='{DynamicResource panel}' BorderBrush='{DynamicResource border}' BorderThickness='0,0,0,1' Padding='14,10'>
			<DockPanel>
				<StackPanel DockPanel.Dock='Right' Orientation='Horizontal' VerticalAlignment='Center' Margin='12,0,0,0'>
					<Ellipse x:Name='Dot' Width='10' Height='10' Margin='0,0,8,0'/>
					<TextBlock x:Name='Status' Foreground='{DynamicResource textDim}' TextTrimming='CharacterEllipsis'/>
				</StackPanel>
				<TextBlock FontSize='16' FontWeight='Bold' VerticalAlignment='Center'>Heaven's Animator <Run Foreground='{DynamicResource accent2}' FontWeight='SemiBold'>Video Reference</Run></TextBlock>
			</DockPanel>
		</Border>
		<Border DockPanel.Dock='Right' Width='270' Background='{DynamicResource panel}' BorderBrush='{DynamicResource border}' BorderThickness='1,0,0,0'>
			<ScrollViewer VerticalScrollBarVisibility='Auto'>
				<StackPanel Margin='14,12'>
					<Button x:Name='Open' Background='{DynamicResource accent}' BorderBrush='{DynamicResource accent}' Foreground='{DynamicResource accentText}' FontWeight='SemiBold'>Open Video…</Button>
					<TextBlock x:Name='FileName' Margin='0,10,0,0' TextTrimming='CharacterEllipsis' Text='No video yet'/>
					<TextBlock x:Name='Info' Foreground='{DynamicResource textDim}' Margin='0,2,0,12' TextWrapping='Wrap' Text='Open or drop a video'/>
					<Rectangle Style='{StaticResource Rule}'/>
					<DockPanel Style='{StaticResource Row}' ToolTip='The Studio frame where the video starts (Up / Down: ±1)'>
						<TextBox x:Name='Start' DockPanel.Dock='Right'/>
						<TextBlock Text='Start' VerticalAlignment='Center'/>
					</DockPanel>
					<DockPanel Style='{StaticResource Row}'>
						<TextBlock DockPanel.Dock='Left' Text='Earlier' Foreground='{DynamicResource textDim}' VerticalAlignment='Center' Margin='0,0,4,0'/>
						<TextBlock DockPanel.Dock='Right' Text='Later' Foreground='{DynamicResource textDim}' VerticalAlignment='Center' Margin='4,0,0,0'/>
						<UniformGrid Rows='1'>
							<Button x:Name='Minus10' Style='{StaticResource Small}'>-10</Button>
							<Button x:Name='Minus1' Style='{StaticResource Small}'>-1</Button>
							<Button x:Name='Plus1' Style='{StaticResource Small}'>+1</Button>
							<Button x:Name='Plus10' Style='{StaticResource Small}'>+10</Button>
						</UniformGrid>
					</DockPanel>
					<Button x:Name='StartHere' Margin='0,0,0,12' ToolTip=""Start the video at Studio's current frame"">Start Here</Button>
					<Rectangle Style='{StaticResource Rule}'/>
					<DockPanel Style='{StaticResource Row}' ToolTip='Video frames skipped at its start (Up / Down: ±1)'>
						<TextBox x:Name='Trim' DockPanel.Dock='Right'/>
						<TextBlock Text='Trim' VerticalAlignment='Center'/>
					</DockPanel>
					<DockPanel Style='{StaticResource Row}' ToolTip='How fast the video plays against Studio (1 = real time)'>
						<TextBox x:Name='Speed' DockPanel.Dock='Right'/>
						<TextBlock Text='Speed' VerticalAlignment='Center'/>
					</DockPanel>
					<UniformGrid Rows='1' Margin='0,0,0,8'>
						<ToggleButton x:Name='Loop' Margin='0,0,3,0' ToolTip='Play the video again from Start when it ends'>Loop</ToggleButton>
						<ToggleButton x:Name='Mute' Margin='3,0,0,0' ToolTip=""Silence the video's sound"">Mute</ToggleButton>
					</UniformGrid>
					<DockPanel Style='{StaticResource Row}' ToolTip='0 to 1'>
						<TextBox x:Name='Volume' DockPanel.Dock='Right'/>
						<TextBlock Text='Volume' VerticalAlignment='Center'/>
					</DockPanel>
					<Rectangle Style='{StaticResource Rule}'/>
					<UniformGrid Rows='1' Margin='0,0,0,12'>
						<ToggleButton x:Name='Mirror' Margin='0,0,3,0' ToolTip='Flip the video left / right'>Mirror</ToggleButton>
						<ToggleButton x:Name='Top' Margin='3,0,0,0' ToolTip='Keep this window above Studio'>Always on top</ToggleButton>
					</UniformGrid>
					<Rectangle Style='{StaticResource Rule}'/>
					<TextBlock x:Name='Readout' Foreground='{DynamicResource textDim}' TextWrapping='Wrap'/>
					<TextBlock Foreground='{DynamicResource textDim}' TextWrapping='Wrap' Margin='0,8,0,0' FontSize='13'
							   Text='Middle mouse: pan · Wheel: zoom · Home: fit'/>
				</StackPanel>
			</ScrollViewer>
		</Border>
		<Grid x:Name='View' ClipToBounds='True' Background='{DynamicResource bg}'>
			<Grid x:Name='Content'>
				<Image x:Name='Frame' Stretch='Uniform' RenderOptions.BitmapScalingMode='Linear' RenderTransformOrigin='0.5,0.5'/>
			</Grid>
			<Grid x:Name='Drop' MaxWidth='460' Margin='16' HorizontalAlignment='Center' VerticalAlignment='Center'>
				<Rectangle Stroke='{DynamicResource border}' StrokeThickness='2' StrokeDashArray='4 3' RadiusX='4' RadiusY='4'/>
				<StackPanel Margin='28,40'>
					<TextBlock x:Name='DropTitle' FontSize='16' FontWeight='SemiBold' HorizontalAlignment='Center' Margin='0,0,0,6'/>
					<TextBlock x:Name='DropText' Foreground='{DynamicResource textDim}' TextWrapping='Wrap' TextAlignment='Center'/>
				</StackPanel>
			</Grid>
		</Grid>
	</DockPanel>
</Window>";

	public readonly Window Window;
	readonly Image image;
	readonly FrameworkElement view, content, drop;
	readonly TextBlock dropTitle, dropText, status, readout, fileName, info;
	readonly Ellipse dot;
	readonly TextBox startBox, trimBox, speedBox, volumeBox;
	readonly ToggleButton loopBtn, muteBtn, mirrorBtn, topBtn;
	readonly int port;
	readonly HttpListener listener;
	readonly string startError;
	readonly JavaScriptSerializer json = new JavaScriptSerializer();
	readonly VideoDecoder decoder = new VideoDecoder();

	// Studio's state, from the newest /sync
	double frame, fps = 60, speed = 1;
	bool playing;
	int seq;
	DateTime lastSync = DateTime.MinValue;

	// the sidebar (the Blender plugin's settings)
	double start, playRate = 1, volume = 1;
	int trim;

	// the open video
	string videoPath;
	VideoInfo video;
	int requested = -1;
	WriteableBitmap bitmap;
	MediaPlayer audio; // only while unmuted: Media Foundation decodes the picture, this plays the sound
	bool audioPlaying;

	// pan / zoom
	double zoom = 1, panX, panY;
	Point? panFrom;

	public Player(int port, string startVideo)
	{
		this.port = port;
		Window = (Window)XamlReader.Parse(XAML);
		image = (Image)Find("Frame");
		view = (FrameworkElement)Find("View");
		content = (FrameworkElement)Find("Content");
		drop = (FrameworkElement)Find("Drop");
		dropTitle = (TextBlock)Find("DropTitle");
		dropText = (TextBlock)Find("DropText");
		status = (TextBlock)Find("Status");
		readout = (TextBlock)Find("Readout");
		fileName = (TextBlock)Find("FileName");
		info = (TextBlock)Find("Info");
		dot = (Ellipse)Find("Dot");
		startBox = (TextBox)Find("Start");
		trimBox = (TextBox)Find("Trim");
		speedBox = (TextBox)Find("Speed");
		volumeBox = (TextBox)Find("Volume");
		loopBtn = (ToggleButton)Find("Loop");
		muteBtn = (ToggleButton)Find("Mute");
		mirrorBtn = (ToggleButton)Find("Mirror");
		topBtn = (ToggleButton)Find("Top");
		ShowHint(null);

		var settings = LoadSettings();
		startBox.Text = Setting(settings, "start", "0");
		trimBox.Text = Setting(settings, "trim", "0");
		speedBox.Text = Setting(settings, "speed", "1.00");
		volumeBox.Text = Setting(settings, "volume", "1.00");
		loopBtn.IsChecked = Setting(settings, "loop", "False") == "True";
		muteBtn.IsChecked = Setting(settings, "mute", "True") == "True";
		mirrorBtn.IsChecked = Setting(settings, "mirror", "False") == "True";
		topBtn.IsChecked = Setting(settings, "topmost", "False") == "True";
		ReadFields();
		Window.Topmost = topBtn.IsChecked == true;
		var savedTheme = new Dictionary<string, object>();
		foreach (string part in Setting(settings, "theme", "").Split(';'))
			if (part.IndexOf(':') > 0) savedTheme[part.Substring(0, part.IndexOf(':'))] = part.Substring(part.IndexOf(':') + 1);
		ApplyTheme(savedTheme); // the last theme Studio sent, until it sends one again

		NumberField(startBox, 1, "0", delegate (double v) { return true; });
		NumberField(trimBox, 1, "0", delegate (double v) { return v >= 0; });
		NumberField(speedBox, 0.05, "0.00", delegate (double v) { return v > 0 && v <= 100; });
		NumberField(volumeBox, 0.05, "0.00", delegate (double v) { return v >= 0 && v <= 1; });
		Nudge("Minus10", -10);
		Nudge("Minus1", -1);
		Nudge("Plus1", 1);
		Nudge("Plus10", 10);
		((Button)Find("StartHere")).Click += delegate { startBox.Text = Math.Round(StudioFrame()).ToString(Inv); };
		loopBtn.Click += delegate { SaveSettings(); };
		muteBtn.Click += delegate { SaveSettings(); UpdateAudio(); };
		mirrorBtn.Click += delegate { ApplyLayout(); SaveSettings(); };
		topBtn.Click += delegate { Window.Topmost = topBtn.IsChecked == true; SaveSettings(); };
		((Button)Find("Open")).Click += delegate
		{
			var dlg = new Microsoft.Win32.OpenFileDialog
			{
				Title = "Open a reference video",
				Filter = "Videos|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm|All files|*.*",
			};
			if (dlg.ShowDialog(Window) == true) Open(dlg.FileName);
		};

		// opening a video: the button, drag & drop, or the command line. It plays from disk: nothing is uploaded.
		Window.DragOver += (s, e) =>
		{
			e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		};
		Window.Drop += (s, e) =>
		{
			var files = e.Data.GetData(DataFormats.FileDrop) as string[];
			if (files != null && files.Length > 0) Open(files[0]);
		};
		decoder.Opened = vi => Window.Dispatcher.BeginInvoke(new Action(() => OnOpened(vi)));
		decoder.Failed = why => Window.Dispatcher.BeginInvoke(new Action(() => OnFailed(why)));

		// pan / zoom, like Blender's image editor
		view.MouseWheel += (s, e) =>
		{
			Point c = e.GetPosition(view);
			double z = Math.Max(0.1, Math.Min(40, zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
			panX = c.X - (c.X - panX) * z / zoom;
			panY = c.Y - (c.Y - panY) * z / zoom;
			zoom = z;
			ApplyView();
		};
		view.MouseDown += (s, e) =>
		{
			if (e.ChangedButton != MouseButton.Middle) return;
			panFrom = e.GetPosition(view);
			view.CaptureMouse();
		};
		view.MouseMove += (s, e) =>
		{
			if (panFrom == null) return;
			Point p = e.GetPosition(view);
			panX += p.X - panFrom.Value.X;
			panY += p.Y - panFrom.Value.Y;
			panFrom = p;
			ApplyView();
		};
		view.MouseUp += (s, e) =>
		{
			if (e.ChangedButton != MouseButton.Middle) return;
			panFrom = null;
			view.ReleaseMouseCapture();
		};
		Window.PreviewKeyDown += (s, e) =>
		{
			if (e.Key != Key.Home || Keyboard.FocusedElement is TextBox) return;
			zoom = 1;
			panX = panY = 0;
			ApplyView();
		};

		listener = new HttpListener();
		listener.Prefixes.Add("http://localhost:" + port + "/");
		try
		{
			listener.Start();
		}
		catch (HttpListenerException)
		{
			listener = null;
			startError = "Port " + port + " is busy: is HA Video Reference already open? (README › Another port)";
		}
		Window.Loaded += delegate
		{
			Serve();
			if (startVideo != null) Open(startVideo);
		};
		Window.Closed += delegate
		{
			if (listener != null) listener.Close();
			decoder.Close();
			if (audio != null) audio.Close();
		};
		CompositionTarget.Rendering += delegate { Tick(); };

		var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
		timer.Tick += delegate { ShowStatus(); };
		timer.Start();
		ShowStatus();
	}

	object Find(string name)
	{
		return Window.FindName(name);
	}

	// ---- the sidebar ----

	static bool Parse(string text, out double v)
	{
		return double.TryParse(text, NumberStyles.Float, Inv, out v) && !double.IsNaN(v) && !double.IsInfinity(v);
	}

	// a number box: Up / Down step it; an invalid value is ignored (the last valid one stays in use)
	void NumberField(TextBox box, double step, string format, Func<double, bool> valid)
	{
		box.TextChanged += delegate { ReadFields(); SaveSettings(); };
		box.PreviewKeyDown += (s, e) =>
		{
			if (e.Key != Key.Up && e.Key != Key.Down) return;
			double v;
			if (!Parse(box.Text, out v)) v = 0;
			double next = Math.Round((v + (e.Key == Key.Up ? step : -step)) / step) * step;
			if (valid(next)) box.Text = next.ToString(format, Inv);
			e.Handled = true;
		};
		box.LostFocus += delegate
		{
			double v;
			if (!Parse(box.Text, out v) || !valid(v)) box.Text = Current(box).ToString(format, Inv);
		};
	}

	double Current(TextBox box)
	{
		return box == startBox ? start : box == trimBox ? trim : box == speedBox ? playRate : volume;
	}

	void ReadFields()
	{
		double v;
		if (Parse(startBox.Text, out v)) start = Math.Round(v);
		if (Parse(trimBox.Text, out v) && v >= 0) trim = (int)Math.Round(v);
		if (Parse(speedBox.Text, out v) && v > 0 && v <= 100) playRate = v;
		if (Parse(volumeBox.Text, out v) && v >= 0 && v <= 1) volume = v;
		if (audio != null) audio.Volume = volume;
	}

	void Nudge(string button, int frames)
	{
		((Button)Find(button)).Click += delegate { startBox.Text = (start + frames).ToString(Inv); };
	}

	// ---- the video ----

	void ShowHint(string error)
	{
		drop.Visibility = Visibility.Visible;
		dropTitle.Text = error == null ? "Drop a video here" : "Can't play that video";
		dropText.Text = error ?? "or press Open Video…. It plays from your computer: nothing is uploaded.\n\n" +
			"In Studio: HA Video Reference tab › Video Sync › Connect. Then scrub or play in Heaven's Animator and this video follows.";
	}

	void Open(string path)
	{
		videoPath = path;
		fileName.Text = System.IO.Path.GetFileName(path);
		info.Text = "Opening…";
		decoder.Open(path);
	}

	void OnOpened(VideoInfo vi)
	{
		if (vi.Path != videoPath) return; // another video was opened since
		video = vi;
		requested = -1;
		drop.Visibility = Visibility.Collapsed;
		info.Text = string.Format(Inv, "{0} frames at {1:0.##} fps · {2}×{3}", vi.FrameCount, vi.Fps, vi.Width, vi.Height);
		Window.Title = System.IO.Path.GetFileName(vi.Path) + " · HA Video Reference";
		ApplyLayout();
		UpdateAudio();
	}

	void OnFailed(string why)
	{
		video = null;
		image.Source = bitmap = null;
		info.Text = "Can't play this video";
		ShowHint(why);
		UpdateAudio();
	}

	void ApplyLayout()
	{
		var t = new TransformGroup();
		t.Children.Add(new RotateTransform(video != null ? video.Rotation : 0));
		t.Children.Add(new ScaleTransform(mirrorBtn.IsChecked == true ? -1 : 1, 1));
		image.LayoutTransform = t;
	}

	void ApplyView()
	{
		content.RenderTransform = new MatrixTransform(zoom, 0, 0, zoom, panX, panY);
	}

	void UpdateAudio()
	{
		bool want = video != null && muteBtn.IsChecked != true;
		if (!want || (audio != null && audio.Source != null && audio.Source.LocalPath != video.Path))
		{
			if (audio != null) audio.Close();
			audio = null;
			audioPlaying = false;
		}
		if (want && audio == null)
		{
			audio = new MediaPlayer { Volume = volume };
			audio.Open(new Uri(video.Path));
		}
	}

	// Studio's frame now: the newest one it sent, plus the time since then while it plays
	double StudioFrame()
	{
		double age = Age();
		return playing && age > 0 && age < 0.5 ? frame + age * fps * speed : frame;
	}

	// Studio frame -> video frame (continuous): Start, Trim, Speed and Loop as in the Blender plugin. Before Start the
	// video holds its first frame; after its end the last one (or loops).
	double VideoFrame(double studioFrame, out bool running)
	{
		double x = (studioFrame - start) * playRate * video.Fps / fps;
		double length = Math.Max(1, video.FrameCount - trim);
		running = x >= 0 && (loopBtn.IsChecked == true || x < length);
		if (x < 0) x = 0;
		if (loopBtn.IsChecked == true) x = x % length;
		else if (x > length - 1) x = length - 1;
		return trim + x;
	}

	void Tick()
	{
		if (video != null)
		{
			bool running;
			double v = VideoFrame(StudioFrame(), out running);
			int n = (int)Math.Floor(v + 1e-6);
			if (n != requested)
			{
				requested = n;
				decoder.Show(n);
			}
			SyncAudio(v / video.Fps, running);
		}
		decoder.Take((pixels, w, h) =>
		{
			if (bitmap == null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
			{
				bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
				image.Source = bitmap;
			}
			bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
		});
	}

	// the sound follows the picture loosely (it is corrected past 0.12 s)
	void SyncAudio(double seconds, bool running)
	{
		if (audio == null) return;
		if (!(playing && running && Age() < 3))
		{
			if (audioPlaying) audio.Pause();
			audioPlaying = false;
			return;
		}
		double rate = speed * playRate;
		if (!audioPlaying)
		{
			audio.Position = TimeSpan.FromSeconds(seconds);
			audio.Play();
			audioPlaying = true;
		}
		else if (Math.Abs(audio.Position.TotalSeconds - seconds) > 0.12)
		{
			audio.Position = TimeSpan.FromSeconds(seconds);
		}
		if (audio.SpeedRatio != rate) audio.SpeedRatio = rate;
	}

	double Age()
	{
		return lastSync == DateTime.MinValue ? -1 : (DateTime.UtcNow - lastSync).TotalSeconds;
	}

	void ShowStatus()
	{
		double age = Age();
		bool live = age >= 0 && age < 3;
		dot.SetResourceReference(Shape.FillProperty, live ? "success" : "warning");
		status.Text = startError ?? (live ? "Synced with Studio"
			: age < 0 ? "Waiting for Studio… (Video Sync › Connect, port " + port + ")" : "Studio went quiet");
		double f = StudioFrame();
		string text = string.Format(Inv, "Studio frame {0} at {1:0.##} fps", Math.Floor(f + 1e-6), fps);
		if (video != null)
		{
			text += f < start
				? string.Format(Inv, "\nThe video starts at frame {0}", start)
				: string.Format(Inv, "\nVideo frame {0} of {1}", requested, video.FrameCount);
		}
		readout.Text = text;
	}

	// ---- the theme: Heaven's Animator's, sent by the plugin as {token = "rrggbb"} (only the tokens the XAML uses) ----

	string themeText = "";

	void ApplyTheme(Dictionary<string, object> theme)
	{
		if (theme == null) return;
		var parts = new List<string>();
		foreach (var pair in theme)
		{
			string hex = pair.Value as string;
			int rgb;
			if (hex == null || hex.Length != 6 || !Window.Resources.Contains(pair.Key)
				|| !int.TryParse(hex, NumberStyles.HexNumber, Inv, out rgb)) continue;
			parts.Add(pair.Key + ":" + hex.ToLowerInvariant());
		}
		parts.Sort(StringComparer.Ordinal);
		string text = string.Join(";", parts);
		if (text == themeText || text == "") return;
		themeText = text;
		foreach (string part in parts)
		{
			int rgb = int.Parse(part.Substring(part.IndexOf(':') + 1), NumberStyles.HexNumber, Inv);
			var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
			brush.Freeze();
			Window.Resources[part.Substring(0, part.IndexOf(':'))] = brush;
		}
		SaveSettings();
	}

	// ---- the local server: requests are handled one at a time on the window's thread (they are tiny) ----

	async void Serve()
	{
		while (listener != null && listener.IsListening)
		{
			HttpListenerContext ctx;
			try
			{
				ctx = await listener.GetContextAsync();
			}
			catch (Exception)
			{
				return; // closed
			}
			try
			{
				await Handle(ctx.Request, ctx.Response);
			}
			catch (Exception e)
			{
				try { Send(ctx.Response, 500, "{\"ok\":false,\"error\":" + json.Serialize(e.Message) + "}"); } catch (Exception) { }
			}
			finally
			{
				try { ctx.Response.Close(); } catch (Exception) { }
			}
		}
	}

	async Task Handle(HttpListenerRequest req, HttpListenerResponse res)
	{
		string path = req.Url.AbsolutePath;
		string ping = "{\"ok\":true,\"app\":\"HA Video Reference\",\"version\":" + VERSION + "}";
		if (req.HttpMethod == "POST" && path == "/sync")
		{
			if (req.ContentLength64 > 65536)
			{
				Send(res, 413, "{\"ok\":false,\"error\":\"too large\"}");
				return;
			}
			string body;
			using (var reader = new StreamReader(req.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync();
			Dictionary<string, object> d;
			try
			{
				d = json.DeserializeObject(body) as Dictionary<string, object>;
			}
			catch (ArgumentException)
			{
				d = null;
			}
			if (d == null)
			{
				Send(res, 400, "{\"ok\":false,\"error\":\"expected a JSON object\"}");
				return;
			}
			double v;
			if (Number(d, "frame", out v)) frame = v;
			if (Number(d, "fps", out v) && v > 0) fps = v;
			if (Number(d, "speed", out v) && v > 0) speed = v;
			object p;
			if (d.TryGetValue("playing", out p) && p is bool) playing = (bool)p;
			if (d.TryGetValue("theme", out p)) ApplyTheme(p as Dictionary<string, object>);
			seq++;
			lastSync = DateTime.UtcNow;
			Send(res, 200, ping);
		}
		else if (path == "/state")
		{
			Send(res, 200, string.Format(Inv, "{{\"frame\":{0:R},\"fps\":{1:R},\"playing\":{2},\"speed\":{3:R},\"seq\":{4},\"age\":{5:R}",
				frame, fps, playing ? "true" : "false", speed, seq, Age()) + "}"); // "}" outside: "{5:R}}}" would be read as the format "R}"
		}
		else if (path == "/ping")
		{
			Send(res, 200, ping);
		}
		else
		{
			Send(res, 404, "{\"ok\":false,\"error\":\"not found\"}");
		}
	}

	static bool Number(Dictionary<string, object> d, string key, out double v)
	{
		v = 0;
		object o;
		if (!d.TryGetValue(key, out o) || o == null || o is bool || o is string) return false;
		try
		{
			v = Convert.ToDouble(o, Inv);
		}
		catch (Exception)
		{
			return false;
		}
		return !double.IsNaN(v) && !double.IsInfinity(v);
	}

	static void Send(HttpListenerResponse res, int code, string body)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(body);
		res.StatusCode = code;
		res.ContentType = "application/json; charset=utf-8";
		res.Headers["Cache-Control"] = "no-store";
		res.ContentLength64 = bytes.Length;
		res.OutputStream.Write(bytes, 0, bytes.Length);
	}

	// ---- settings: one key=value per line in %APPDATA%\HA Video Reference ----

	static Dictionary<string, string> LoadSettings()
	{
		var d = new Dictionary<string, string>();
		try
		{
			foreach (string line in File.ReadAllLines(SettingsPath))
			{
				int i = line.IndexOf('=');
				if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
			}
		}
		catch (Exception) { } // none yet
		return d;
	}

	static string Setting(Dictionary<string, string> d, string key, string fallback)
	{
		string v;
		return d.TryGetValue(key, out v) ? v : fallback;
	}

	void SaveSettings()
	{
		try
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
			File.WriteAllLines(SettingsPath, new[] {
				"start=" + start.ToString(Inv),
				"trim=" + trim.ToString(Inv),
				"speed=" + playRate.ToString("0.00", Inv),
				"volume=" + volume.ToString("0.00", Inv),
				"loop=" + (loopBtn.IsChecked == true),
				"mute=" + (muteBtn.IsChecked == true),
				"mirror=" + (mirrorBtn.IsChecked == true),
				"topmost=" + (topBtn.IsChecked == true),
				"theme=" + themeText,
			});
		}
		catch (Exception) { } // a read-only profile: the settings just aren't remembered
	}
}

class VideoInfo
{
	public string Path;
	public int Width, Height, FrameCount, Rotation;
	public double Fps;
}

// Decodes exact frames with Media Foundation's Source Reader on its own thread: the window asks for a frame number
// (Show), the decoder seeks (or reads on, when the frame is a little ahead) until that frame's time and hands the picture
// back (Take). Frame n is the one at (first frame's time + n / fps), so a video whose first frame is not at 0 (MP4 edit
// lists) still starts at frame 0.
class VideoDecoder
{
	public Action<VideoInfo> Opened;
	public Action<string> Failed;

	readonly object sync = new object(), frameLock = new object();
	string pendingOpen;
	int want = -1;
	bool closing;
	Thread thread;

	// the newest picture (under frameLock)
	byte[] pixels;
	int pixelsW, pixelsH;
	bool fresh;

	// decoder thread only
	IMFSourceReader reader;
	VideoInfo info;
	int fullW, fullH, cropX, cropY, stride;
	long firstTs, durationTicks;
	int shown = -1;
	bool atEnd;

	public void Open(string path)
	{
		lock (sync)
		{
			pendingOpen = path;
			want = -1;
			if (thread == null)
			{
				thread = new Thread(Run) { IsBackground = true, Name = "Video decoder" };
				thread.SetApartmentState(ApartmentState.MTA);
				thread.Start();
			}
			Monitor.Pulse(sync);
		}
	}

	public void Show(int frame)
	{
		lock (sync)
		{
			want = frame;
			Monitor.Pulse(sync);
		}
	}

	public void Close()
	{
		lock (sync)
		{
			closing = true;
			Monitor.Pulse(sync);
		}
	}

	// hands the newest picture to draw (on the window's thread), if there is a new one
	public void Take(Action<byte[], int, int> draw)
	{
		lock (frameLock)
		{
			if (!fresh) return;
			fresh = false;
			draw(pixels, pixelsW, pixelsH);
		}
	}

	void Run()
	{
		try
		{
			MF.MFStartup(MF.VERSION, 0);
		}
		catch (Exception e)
		{
			if (Failed != null) Failed("Windows' video decoder (Media Foundation) is missing: on Windows N, install the Media Feature Pack. " + e.Message);
			return;
		}
		while (true)
		{
			string open;
			int n;
			lock (sync)
			{
				while (!closing && pendingOpen == null && (reader == null || want < 0 || want == shown)) Monitor.Wait(sync);
				if (closing) break;
				open = pendingOpen;
				pendingOpen = null;
				n = want;
			}
			if (open != null)
			{
				OpenReader(open);
				continue;
			}
			try
			{
				DecodeTo(n);
			}
			catch (Exception)
			{
				shown = n; // a damaged spot: keep the last picture
				atEnd = true;
			}
		}
		Release(reader);
	}

	void OpenReader(string path)
	{
		Release(reader);
		reader = null;
		shown = -1;
		try
		{
			IMFAttributes attrs;
			MF.MFCreateAttributes(out attrs, 1);
			// CPU decoding: the GPU's (DXVA) measured more memory (287 vs 208 MB). Not MF_LOW_LATENCY: it saves memory
			// but hands B-frame videos' frames out of order (measured: 3 frames late).
			attrs.SetUINT32(ref MF.SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1); // YUV -> RGB32 in the reader
			IMFSourceReader r;
			MF.MFCreateSourceReaderFromURL(path, attrs, out r);
			Release(attrs);
			r.SetStreamSelection(MF.ALL_STREAMS, false);
			r.SetStreamSelection(MF.FIRST_VIDEO_STREAM, true);
			var vi = new VideoInfo { Path = path };
			IMFMediaType native;
			if (r.GetNativeMediaType(MF.FIRST_VIDEO_STREAM, 0, out native) >= 0)
			{
				int rotation;
				if (native.GetUINT32(ref MF.MT_VIDEO_ROTATION, out rotation) >= 0) vi.Rotation = rotation;
				Release(native);
			}
			IMFMediaType rgb;
			MF.MFCreateMediaType(out rgb);
			rgb.SetGUID(ref MF.MT_MAJOR_TYPE, ref MF.MediaType_Video);
			rgb.SetGUID(ref MF.MT_SUBTYPE, ref MF.VideoFormat_RGB32);
			r.SetCurrentMediaType(MF.FIRST_VIDEO_STREAM, IntPtr.Zero, rgb);
			Release(rgb);
			reader = r;
			info = vi;
			ReadFormat();
			PropVariant duration;
			durationTicks = r.GetPresentationAttribute(MF.MEDIASOURCE, ref MF.PD_DURATION, out duration) >= 0 ? duration.value : 0;
			long ts;
			IMFSample first = Next(out ts);
			if (first == null) throw new InvalidDataException("the video has no frames");
			firstTs = ts;
			Publish(first);
			Release(first);
			shown = 0;
			vi.FrameCount = Math.Max(1, (int)Math.Round(durationTicks * info.Fps / 1e7));
			if (Opened != null) Opened(vi);
		}
		catch (Exception e)
		{
			Release(reader);
			reader = null;
			if (Failed != null) Failed(e is COMException ? "Windows can't open it (error 0x" + ((COMException)e).ErrorCode.ToString("X8") + "). An mp4 (H.264) always works." : e.Message);
		}
	}

	// the current output format: size, crop (display aperture), stride, frame rate
	void ReadFormat()
	{
		IMFMediaType t;
		reader.GetCurrentMediaType(MF.FIRST_VIDEO_STREAM, out t);
		try
		{
			long size, rate;
			t.GetUINT64(ref MF.MT_FRAME_SIZE, out size);
			fullW = (int)(size >> 32);
			fullH = (int)(size & 0xFFFFFFFF);
			if (t.GetUINT32(ref MF.MT_DEFAULT_STRIDE, out stride) < 0) stride = fullW * 4;
			info.Fps = t.GetUINT64(ref MF.MT_FRAME_RATE, out rate) >= 0 && (rate & 0xFFFFFFFF) != 0 && (rate >> 32) != 0
				? (double)(rate >> 32) / (rate & 0xFFFFFFFF) : 30;
			cropX = cropY = 0;
			info.Width = fullW;
			info.Height = fullH;
			var area = new byte[16]; // MFVideoArea: OffsetX {fract, value}, OffsetY {fract, value}, cx, cy
			int got;
			if (t.GetBlob(ref MF.MT_MINIMUM_DISPLAY_APERTURE, area, area.Length, out got) >= 0 && got >= 16)
			{
				int x = BitConverter.ToInt16(area, 2), y = BitConverter.ToInt16(area, 6);
				int w = BitConverter.ToInt32(area, 8), h = BitConverter.ToInt32(area, 12);
				if (x >= 0 && y >= 0 && w > 0 && h > 0 && x + w <= fullW && y + h <= fullH)
				{
					cropX = x;
					cropY = y;
					info.Width = w;
					info.Height = h;
				}
			}
		}
		finally
		{
			Release(t);
		}
	}

	long FrameTime(int n)
	{
		return firstTs + (long)Math.Round(n * 1e7 / info.Fps);
	}

	void Seek(long time)
	{
		// the last frames can sit past the stated duration (B-frame delay), and a seek past it is refused
		if (durationTicks > 0) time = Math.Min(time, durationTicks - 1);
		var pos = new PropVariant { vt = 20, value = Math.Max(0, time) }; // VT_I8, 100 ns units
		Guid format = Guid.Empty;
		reader.SetCurrentPosition(ref format, ref pos);
		atEnd = false;
	}

	void DecodeTo(int n)
	{
		long target = FrameTime(n), half = (long)(0.5e7 / info.Fps);
		bool readOn = shown >= 0 && !atEnd && n > shown && n - shown <= Math.Max(1, (int)info.Fps); // within a second ahead
		long back = 0;
		bool fresh = !readOn;
		if (!readOn) Seek(target);
		IMFSample last = null;
		try
		{
			while (true)
			{
				long ts;
				IMFSample s = Next(out ts);
				if (fresh && (s == null || ts - half > target) && target - back > firstTs && back < 600 * 10000000L)
				{
					Release(s); // the seek landed after the frame (or at the end): seek further back and read on
					back = back == 0 ? 2 * 10000000L : back * 3;
					Seek(target - back);
					continue;
				}
				fresh = false;
				if (s == null)
				{
					atEnd = true; // past the last frame: show the last one
					if (last != null) Publish(last);
					break;
				}
				if (ts + half >= target)
				{
					Publish(s);
					Release(s);
					break;
				}
				Release(last);
				last = s;
			}
		}
		finally
		{
			Release(last);
		}
		shown = n;
	}

	// the next video sample (null at the end of the stream)
	IMFSample Next(out long ts)
	{
		while (true)
		{
			int actual, flags;
			IMFSample s;
			reader.ReadSample(MF.FIRST_VIDEO_STREAM, 0, out actual, out flags, out ts, out s);
			if ((flags & MF.READERF_CURRENTMEDIATYPECHANGED) != 0) ReadFormat();
			if ((flags & (MF.READERF_ENDOFSTREAM | MF.READERF_ERROR)) != 0)
			{
				Release(s);
				return null;
			}
			if (s != null) return s;
		}
	}

	void Publish(IMFSample sample)
	{
		IMFMediaBuffer buffer;
		sample.ConvertToContiguousBuffer(out buffer);
		try
		{
			var buffer2D = buffer as IMF2DBuffer;
			IntPtr scan0;
			int pitch;
			if (buffer2D != null)
			{
				buffer2D.Lock2D(out scan0, out pitch);
			}
			else
			{
				int max, length;
				buffer.Lock(out scan0, out max, out length);
				pitch = stride;
				if (pitch < 0) scan0 = new IntPtr(scan0.ToInt64() + (long)(fullH - 1) * -pitch); // bottom-up
			}
			try
			{
				int w = info.Width, h = info.Height;
				lock (frameLock)
				{
					if (pixels == null || pixels.Length != w * h * 4) pixels = new byte[w * h * 4];
					for (int y = 0; y < h; y++)
						Marshal.Copy(new IntPtr(scan0.ToInt64() + (long)(cropY + y) * pitch + cropX * 4), pixels, y * w * 4, w * 4);
					pixelsW = w;
					pixelsH = h;
					fresh = true;
				}
			}
			finally
			{
				if (buffer2D != null) buffer2D.Unlock2D();
				else buffer.Unlock();
			}
		}
		finally
		{
			Release(buffer);
		}
	}

	// samples hold big picture buffers: hand them back at once instead of waiting for the garbage collector
	static void Release(object com)
	{
		if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
	}
}

// ---- Media Foundation interop (mfplat.dll / mfreadwrite.dll, part of Windows) ----
// Unused vtable slots are declared as placeholders (_n): only their position matters, they are never called.

static class MF
{
	public const int VERSION = 0x00020070;
	public const int FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC), ALL_STREAMS = unchecked((int)0xFFFFFFFE), MEDIASOURCE = -1;
	public const int READERF_ERROR = 0x1, READERF_ENDOFSTREAM = 0x2, READERF_CURRENTMEDIATYPECHANGED = 0x20;
	public static Guid SOURCE_READER_ENABLE_VIDEO_PROCESSING = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
	public static Guid MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
	public static Guid MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
	public static Guid MediaType_Video = new Guid("73646976-0000-0010-8000-00aa00389b71");
	public static Guid VideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00aa00389b71");
	public static Guid MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
	public static Guid MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
	public static Guid MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
	public static Guid MT_MINIMUM_DISPLAY_APERTURE = new Guid("d7388766-18fe-48c6-a177-ee894867c8c4");
	public static Guid MT_VIDEO_ROTATION = new Guid("c380465d-2271-428c-9b83-ecea3b4a85c1");
	public static Guid PD_DURATION = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

	[DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
	public static extern void MFStartup(int version, int flags);
	[DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
	public static extern void MFCreateAttributes(out IMFAttributes attributes, int initialSize);
	[DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
	public static extern void MFCreateMediaType(out IMFMediaType type);
	[DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
	public static extern void MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IMFAttributes attributes, out IMFSourceReader reader);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
struct PropVariant
{
	[FieldOffset(0)] public short vt;
	[FieldOffset(8)] public long value;
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFAttributes
{
	void _1(); void _2(); void _3(); void _4();
	[PreserveSig] int GetUINT32([In] ref Guid key, out int value);
	[PreserveSig] int GetUINT64([In] ref Guid key, out long value);
	void _7(); void _8(); void _9(); void _10(); void _11(); void _12();
	[PreserveSig] int GetBlob([In] ref Guid key, [Out] byte[] buffer, int size, out int written);
	void _14(); void _15(); void _16(); void _17(); void _18();
	void SetUINT32([In] ref Guid key, int value);
	void _20(); void _21();
	void SetGUID([In] ref Guid key, [In] ref Guid value);
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType // : IMFAttributes (only the attribute calls are used)
{
	void _1(); void _2(); void _3(); void _4();
	[PreserveSig] int GetUINT32([In] ref Guid key, out int value);
	[PreserveSig] int GetUINT64([In] ref Guid key, out long value);
	void _7(); void _8(); void _9(); void _10(); void _11(); void _12();
	[PreserveSig] int GetBlob([In] ref Guid key, [Out] byte[] buffer, int size, out int written);
	void _14(); void _15(); void _16(); void _17(); void _18();
	void SetUINT32([In] ref Guid key, int value);
	void _20(); void _21();
	void SetGUID([In] ref Guid key, [In] ref Guid value);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample // : IMFAttributes (30 methods), then the sample's own
{
	void _1(); void _2(); void _3(); void _4(); void _5(); void _6(); void _7(); void _8(); void _9(); void _10();
	void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19(); void _20();
	void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29(); void _30();
	void _31(); void _32(); void _33(); void _34(); void _35(); void _36(); void _37(); void _38();
	void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
}

[ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer
{
	void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
	void Unlock();
}

[ComImport, Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMF2DBuffer
{
	void Lock2D(out IntPtr scanline0, out int pitch);
	void Unlock2D();
}

[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSourceReader
{
	void _1();
	void SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
	[PreserveSig] int GetNativeMediaType(int stream, int index, out IMFMediaType type);
	void GetCurrentMediaType(int stream, out IMFMediaType type);
	void SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
	void SetCurrentPosition([In] ref Guid format, [In] ref PropVariant position);
	void ReadSample(int stream, int control, out int actualStream, out int flags, out long timestamp, out IMFSample sample);
	void _8(); void _9();
	[PreserveSig] int GetPresentationAttribute(int stream, [In] ref Guid key, out PropVariant value);
}
