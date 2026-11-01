using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

namespace MyAccountingApp;

[Activity(Theme = "@style/AppSplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	EditText? _editOnDown;
	float _downX, _downY;

	/// <summary>
	/// Hides the soft keyboard when the user taps outside the focused text field
	/// (Android keeps it open otherwise). Tapping another text field keeps it open,
	/// and scroll gestures are ignored so the form can be scrolled while typing.
	/// </summary>
	public override bool DispatchTouchEvent(MotionEvent? e)
	{
		if (e?.Action == MotionEventActions.Down)
		{
			_editOnDown = CurrentFocus is EditText edit && !IsInside(edit, e) ? edit : null;
			_downX = e.RawX;
			_downY = e.RawY;
		}

		var handled = base.DispatchTouchEvent(e);

		if (e?.Action == MotionEventActions.Up && _editOnDown is { } previous)
		{
			_editOnDown = null;
			var slop = ViewConfiguration.Get(this)?.ScaledTouchSlop ?? 16;
			var isTap = Math.Abs(e.RawX - _downX) <= slop && Math.Abs(e.RawY - _downY) <= slop;
			var focusMovedToOtherEdit = CurrentFocus is EditText now && now != previous;

			if (isTap && !focusMovedToOtherEdit)
			{
				var imm = (InputMethodManager?)GetSystemService(Context.InputMethodService);
				imm?.HideSoftInputFromWindow(previous.WindowToken, HideSoftInputFlags.None);
				previous.ClearFocus();
			}
		}

		return handled;
	}

	static bool IsInside(Android.Views.View view, MotionEvent e)
	{
		var location = new int[2];
		view.GetLocationOnScreen(location);
		return e.RawX >= location[0] && e.RawX <= location[0] + view.Width
			&& e.RawY >= location[1] && e.RawY <= location[1] + view.Height;
	}
}
