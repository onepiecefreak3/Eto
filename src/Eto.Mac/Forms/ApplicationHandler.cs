using System;
using Eto.Forms;
using Eto.Mac.Forms.Actions;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Mac.Drawing;
using System.Runtime.CompilerServices;

#if XAMMAC2
using AppKit;
using Foundation;
using CoreGraphics;
using ObjCRuntime;
using CoreAnimation;
using CoreImage;
using CoreFoundation;
#else
using MonoMac;
using MonoMac.AppKit;
using MonoMac.Foundation;
using MonoMac.CoreGraphics;
using MonoMac.ObjCRuntime;
using MonoMac.CoreAnimation;
using MonoMac.CoreImage;
using MonoMac.CoreFoundation;
#if Mac64
using nfloat = System.Double;
using nint = System.Int64;
using nuint = System.UInt64;
#else
using nfloat = System.Single;
using nint = System.Int32;
using nuint = System.UInt32;
#endif
#if SDCOMPAT
using CGSize = System.Drawing.SizeF;
using CGRect = System.Drawing.RectangleF;
using CGPoint = System.Drawing.PointF;
#endif
#endif

namespace Eto.Mac.Forms
{
	public class ApplicationHandler : WidgetHandler<NSApplication, Application, Application.ICallback>, Application.IHandler
	{
		bool attached;

		internal static bool QueueResizing { get; set; }

		public NSApplicationDelegate AppDelegate { get; set; }

		public bool AddFullScreenMenuItem { get; set; }

		public bool AllowClosingMainForm { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether native macOS crash reports are generated for uncaught .NET exceptions.
		/// </summary>
		/// <remarks>
		/// By default, this will be true when NOT debugging in Xamarin Studio.
		/// 
		/// The crash report will also include the .NET exception details, which can be extremely useful to determine 
		/// where crashes occurred.
		/// 
		/// When attaching to existing applications, this is assumed to be dealt with by native code and will not be 
		/// enabled regardless.
		/// </remarks>
		/// <value><c>true</c> to enable native crash reports; otherwise, <c>false</c>.</value>
		public bool EnableNativeCrashReport { get; set; } = !System.Diagnostics.Debugger.IsAttached;

#if Mac64
		/// <summary>
		/// Gets or sets a value indicating whether we should translate native exceptions into .NET so it shows the .net stack trace
		/// </summary>
		/// <remarks>
		/// This allows us to get the stack trace of the .NET Code if it calls something that causes a native crash.
		/// 
		/// There is currently no way to catch the translated exceptions, however the crash report will be much more useful.
		/// </remarks>
		/// <value><c>true</c> to enable native crash translation; otherwise, <c>false</c></value>
		public bool EnableNativeExceptionTranslation { get; set; } = true;
#endif

		public ApplicationHandler()
		{
			Control = NSApplication.SharedApplication;
		}

		public static ApplicationHandler Instance
		{
			get { return Application.Instance == null ? null : Application.Instance.Handler as ApplicationHandler; }
		}

		/// <summary>
		/// Event to inject custom functionality during the event loop of a modal session for an eto dialog.
		/// </summary>
		public event EventHandler<ModalEventArgs> ProcessModalSession;

		/// <summary>
		/// Raises the <see cref="ProcessModalSession"/> event.
		/// </summary>
		/// <param name="e">Event arguments</param>
		protected virtual void OnProcessModalSession(ModalEventArgs e)
		{
			if (ProcessModalSession != null)
				ProcessModalSession(this, e);
		}

		internal void TriggerProcessModalSession(ModalEventArgs e)
		{
			OnProcessModalSession(e);
		}

		public string BadgeLabel
		{
			get
			{
				var badgeLabel = Control.DockTile.BadgeLabel;
				return string.IsNullOrEmpty(badgeLabel) ? null : badgeLabel;
			}
			set
			{
				Control.DockTile.BadgeLabel = value ?? string.Empty;
				Control.DockTile.Display();
			}
		}

		public bool ShouldCloseForm(Window window, bool wasClosed)
		{
			if (ReferenceEquals(window, Widget.MainForm))
			{
				if (AllowClosingMainForm && wasClosed)
					Widget.MainForm = null;
				return AllowClosingMainForm;
			}

			return true;
		}

		protected override NSApplication CreateControl()
		{
			return NSApplication.SharedApplication;
		}

		protected override bool DisposeControl { get { return false; } }

		static void restart_WillTerminate(object sender, EventArgs e)
		{
			// re-open after we terminate
			var args = new string[]
			{
				"-c",
				"open \"$1\"", 
				string.Empty,
				NSBundle.MainBundle.BundlePath
			};
			NSTask.LaunchFromPath("/bin/sh", args);
		}

		public void Invoke(Action action)
		{
			if (NSThread.IsMain)
				action();
			else
				DispatchQueue.MainQueue.DispatchSync(action);
		}

		public void AsyncInvoke(Action action)
		{
			DispatchQueue.MainQueue.DispatchAsync(action);
		}

		public void Restart()
		{
			// prevent System.InvalidOperationException:
			// Event registration is overwriting existing delegate.
			// Either just use events or your own delegate: Eto.Mac.AppDelegate
			var oldDelegate = Control.Delegate;
			Control.Delegate = null;
			NSApplication.SharedApplication.WillTerminate += restart_WillTerminate;
			NSApplication.SharedApplication.Terminate(AppDelegate);

			// only get here if cancelled, remove event to restart
			NSApplication.SharedApplication.WillTerminate -= restart_WillTerminate;
			Control.Delegate = oldDelegate;
		}

		public void RunIteration()
		{
			var evt = NSApplication.SharedApplication.NextEvent(NSEventMask.AnyEvent, NSDate.DistantFuture, NSRunLoop.NSDefaultRunLoopMode, true);
			// need to actually send the event
			NSApplication.SharedApplication.SendEvent(evt);
		}

		public void Attach(object context)
		{
			attached = true;
		}

		public void OnMainFormChanged()
		{
		}

		public void Run()
		{
			if (!attached)
			{
				if (EnableNativeCrashReport)
					CrashReporter.Attach();

#if Mac64
				// convert objective-c exceptions into .NET exceptions
				if (EnableNativeExceptionTranslation)
					NSSetUncaughtExceptionHandler(UncaughtExceptionHandler);
#endif


				EtoBundle.Init();

				EtoFontManager.Install();

				if (Control.Delegate == null)
					Control.Delegate = AppDelegate ?? new AppDelegate();
				NSApplication.Main(new string[0]);
			}
			else
				Initialize(Control.Delegate as NSApplicationDelegate);
		}

		public void Initialize(NSApplicationDelegate appdelegate)
		{
			AppDelegate = appdelegate;
			Callback.OnInitialized(Widget, EventArgs.Empty);
		}

		public void Quit()
		{
			NSApplication.SharedApplication.Terminate((NSObject)AppDelegate ?? NSApplication.SharedApplication);
		}

		public bool QuitIsSupported { get { return true; } }

		public void Open(string url)
		{
			NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
		}

#if Mac64
		delegate void UncaughtExceptionHandlerDelegate(IntPtr nsexceptionPtr);
		
		[DllImport(Constants.FoundationLibrary)]
		static extern void NSSetUncaughtExceptionHandler(UncaughtExceptionHandlerDelegate handler);

		static void UncaughtExceptionHandler(IntPtr nsexceptionPtr)
		{
			var nsexception = Runtime.GetNSObject<NSException>(nsexceptionPtr);
			if (nsexception != null)
			{
				if (EtoEnvironment.Platform.IsMono)
				{
					// mono includes full stack already
					throw new ObjCException(nsexception);
				}
				else
				{
					// .NET 5 does not include the full stack as it goes through native code.
					// Fortunately, .NET 5 does actually use the StackTrace property for its Exception.ToString() implementation,
					// so we can feed the stack to the exception object.
					var st = new System.Diagnostics.StackTrace(1); // skip UncaughtException method
					var ststr = st.ToString();
					throw new ObjCException(nsexception, st.ToString());
				}
			}
		}
#endif

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case Application.UnhandledExceptionEvent:
					AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
					break;
				case Application.TerminatingEvent:
					// handled by app delegate
					break;
				case Application.NotificationActivatedEvent:
					if (MacVersion.IsAtLeast(10, 8))
					{
						NSUserNotificationCenter.DefaultUserNotificationCenter.DidActivateNotification += (sender, e) => DidActivateNotification(e.Notification);
						NSApplication.Notifications.ObserveDidFinishLaunching(DidFinishLaunching);
					}
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
		{
			var unhandledExceptionArgs = new UnhandledExceptionEventArgs(e.ExceptionObject, e.IsTerminating);
			Callback.OnUnhandledException(Widget, unhandledExceptionArgs);
		}

		static readonly NSString s_NSApplicationLaunchUserNotificationKey = Dlfcn.GetStringConstant(Messaging.AppKitHandle, "NSApplicationLaunchUserNotificationKey");

		static void DidFinishLaunching(object sender, NSApplicationDidFinishLaunchingEventArgs e)
		{
			NSObject userNotificationObject;
			if (e.Notification.UserInfo.TryGetValue(s_NSApplicationLaunchUserNotificationKey, out userNotificationObject))
			{
				DidActivateNotification(userNotificationObject as NSUserNotification);
			}
		}

		internal static void DidActivateNotification(NSUserNotification notification)
		{
			NSObject idString, dataString;
			if (notification.UserInfo.TryGetValue(NotificationHandler.Info_Id, out idString))
			{
				notification.UserInfo.TryGetValue(NotificationHandler.Info_Data, out dataString);
				var app = ApplicationHandler.Instance;
				app.Callback.OnNotificationActivated(app.Widget, new NotificationEventArgs((NSString)idString, dataString as NSString));
				NSUserNotificationCenter.DefaultUserNotificationCenter.RemoveDeliveredNotification(notification);
			}
		}

		public void EnableFullScreen()
		{
			if (Control.RespondsToSelector(new Selector("setPresentationOptions:")))
			{
				AddFullScreenMenuItem = true;
			}
		}

		public Keys CommonModifier
		{
			get
			{
				return Keys.Application;
			}
		}

		public Keys AlternateModifier
		{
			get
			{
				return Keys.Alt;
			}
		}
	}
}
