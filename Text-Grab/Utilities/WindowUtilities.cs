﻿using Dapplo.Windows.User32;
using Fasetto.Word;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Text_Grab.Extensions;
using Text_Grab.Views;
using static OSInterop;

namespace Text_Grab.Utilities;

public static class WindowUtilities
{
    public static void AddTextToOpenWindow(string textToAdd)
    {
        WindowCollection allWindows = Application.Current.Windows;

        foreach (Window window in allWindows)
            if (window is EditTextWindow mtw)
                mtw.AddThisText(textToAdd);
    }

    public static void SetWindowPosition(Window passedWindow)
    {
        string storedPositionString = "";

        if (passedWindow is EditTextWindow)
            storedPositionString = AppUtilities.TextGrabSettings.EditTextWindowSizeAndPosition;

        if (passedWindow is GrabFrame)
            storedPositionString = AppUtilities.TextGrabSettings.GrabFrameWindowSizeAndPosition;

        List<string> storedPosition = new(storedPositionString.Split(','));

        bool isStoredRectWithinScreen = false;

        if (storedPosition != null
            && storedPosition.Count == 4)
        {
            bool couldParseX = double.TryParse(storedPosition[0], out double parsedX);
            bool couldParseY = double.TryParse(storedPosition[1], out double parsedY);
            bool couldParseW = double.TryParse(storedPosition[2], out double parsedWid);
            bool couldParseH = double.TryParse(storedPosition[3], out double parsedHei);

            bool couldParseAll = couldParseX && couldParseY && couldParseW && couldParseH;

            Rect storedSize = new((int)parsedX, (int)parsedY, (int)parsedWid, (int)parsedHei);
            DisplayInfo[] allScreens = DisplayInfo.AllDisplayInfos;

            if (parsedHei < 10 || parsedWid < 10)
                return;

            foreach (DisplayInfo screen in allScreens)
            {
                Rect screenRect = screen.Bounds;
                DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(passedWindow);
                screenRect = screenRect.GetScaledDownByDpi(dpi);
                if (screenRect.IntersectsWith(storedSize))
                    isStoredRectWithinScreen = true;
            }

            if (isStoredRectWithinScreen && couldParseAll)
            {
                passedWindow.Left = storedSize.X;
                passedWindow.Top = storedSize.Y;
                passedWindow.Width = storedSize.Width;
                passedWindow.Height = storedSize.Height;

                return;
            }
        }
    }

    public static void LaunchFullScreenGrab(TextBox? destinationTextBox = null)
    {
        DisplayInfo[] allScreens = DisplayInfo.AllDisplayInfos;
        WindowCollection allWindows = Application.Current.Windows;

        List<FullscreenGrab> allFullscreenGrab = new();

        int numberOfScreens = allScreens.Count();

        foreach (Window window in allWindows)
            if (window is FullscreenGrab grab)
                allFullscreenGrab.Add(grab);

        int numberOfFullscreenGrabWindowsToCreate = numberOfScreens - allFullscreenGrab.Count;

        for (int i = 0; i < numberOfFullscreenGrabWindowsToCreate; i++)
        {
            allFullscreenGrab.Add(new FullscreenGrab());
        }

        int count = 0;

        double sideLength = 40;

        foreach (DisplayInfo screen in allScreens)
        {
            FullscreenGrab fullScreenGrab = allFullscreenGrab[count];
            fullScreenGrab.WindowStartupLocation = WindowStartupLocation.Manual;
            fullScreenGrab.Width = sideLength;
            fullScreenGrab.Height = sideLength;
            fullScreenGrab.DestinationTextBox = destinationTextBox;
            fullScreenGrab.WindowState = WindowState.Normal;

            Point screenCenterPoint = screen.ScaledCenterPoint();

            fullScreenGrab.Left = screenCenterPoint.X - (sideLength / 2);
            fullScreenGrab.Top = screenCenterPoint.Y - (sideLength / 2);

            fullScreenGrab.Show();
            fullScreenGrab.Activate();

            count++;
        }
    }

    public static Point GetCenterPoint(this DisplayInfo screen)
    {
        Rect screenRect = screen.Bounds;
        double x = screenRect.Left + (screenRect.Width / 2);
        double y = screenRect.Top + (screenRect.Height / 2);
        return new(x, y);
    }

    public static System.Windows.Point GetWindowCenter(this Window window)
    {
        double x = window.Width / 2;
        double y = window.Height / 2;
        return new(x, y);
    }

    public static void CenterOverThisWindow(this Window newWindow, Window bottomWindow)
    {
        System.Windows.Point newWindowCenter = newWindow.GetWindowCenter();
        System.Windows.Point bottomWindowCenter = bottomWindow.GetWindowCenter();

        double newWindowTop = (bottomWindow.Top + bottomWindowCenter.Y) - newWindowCenter.Y;
        double newWindowLeft = (bottomWindow.Left + bottomWindowCenter.X) - newWindowCenter.X;

        newWindow.Top = newWindowTop;
        newWindow.Left = newWindowLeft;
    }

    internal static async void CloseAllFullscreenGrabs()
    {
        WindowCollection allWindows = Application.Current.Windows;

        bool isFromEditWindow = false;
        string stringFromOCR = "";

        foreach (Window window in allWindows)
        {
            if (window is FullscreenGrab fsg)
            {
                if (!string.IsNullOrWhiteSpace(fsg.TextFromOCR))
                    stringFromOCR = fsg.TextFromOCR;

                if (fsg.DestinationTextBox is not null)
                {
                    // TODO 3.0 Find out how to re normalize an ETW when FSG had it minimized 
                    isFromEditWindow = true;
                    // if (fsg.EditWindow.WindowState == WindowState.Minimized)
                    //     fsg.EditWindow.WindowState = WindowState.Normal;
                }

                fsg.Close();
            }
        }

        if (AppUtilities.TextGrabSettings.TryInsert
            && !string.IsNullOrWhiteSpace(stringFromOCR)
            && !isFromEditWindow)
        {
            await TryInsertString(stringFromOCR);
        }

        GC.Collect();
        ShouldShutDown();
    }

    internal static void FullscreenKeyDown(Key key, bool? isActive = null)
    {
        WindowCollection allWindows = Application.Current.Windows;

        if (key == Key.Escape)
            CloseAllFullscreenGrabs();

        foreach (Window window in allWindows)
            if (window is FullscreenGrab fsg)
                fsg.KeyPressed(key, isActive);
    }

    internal static async Task TryInsertString(string stringToInsert)
    {
        await Task.Delay(TimeSpan.FromSeconds(AppUtilities.TextGrabSettings.InsertDelay));

        List<INPUT> inputs = new();
        // make sure keys are up.
        TryInjectModifierKeyUp(ref inputs, VirtualKeyShort.LCONTROL);
        TryInjectModifierKeyUp(ref inputs, VirtualKeyShort.RCONTROL);
        TryInjectModifierKeyUp(ref inputs, VirtualKeyShort.LWIN);
        TryInjectModifierKeyUp(ref inputs, VirtualKeyShort.RWIN);
        TryInjectModifierKeyUp(ref inputs, VirtualKeyShort.LSHIFT);
        TryInjectModifierKeyUp(ref inputs, VirtualKeyShort.RSHIFT);

        // send Ctrl+V (key downs and key ups)
        INPUT ctrlDown = new();
        ctrlDown.Type = OSInterop.InputType.INPUT_KEYBOARD;
        ctrlDown.U.Ki.WVk = VirtualKeyShort.CONTROL;
        inputs.Add(ctrlDown);

        INPUT vDown = new();
        vDown.Type = OSInterop.InputType.INPUT_KEYBOARD;
        vDown.U.Ki.WVk = VirtualKeyShort.KEY_V;
        inputs.Add(vDown);

        INPUT vUp = new();
        vUp.Type = OSInterop.InputType.INPUT_KEYBOARD;
        vUp.U.Ki.WVk = VirtualKeyShort.KEY_V;
        vUp.U.Ki.DwFlags = KEYEVENTF.KEYUP;
        inputs.Add(vUp);

        INPUT ctrlUp = new();
        ctrlUp.Type = OSInterop.InputType.INPUT_KEYBOARD;
        ctrlUp.U.Ki.WVk = VirtualKeyShort.CONTROL;
        ctrlUp.U.Ki.DwFlags = KEYEVENTF.KEYUP;
        inputs.Add(ctrlUp);

        _ = SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
        await Task.CompletedTask;
    }

    private static void TryInjectModifierKeyUp(ref List<INPUT> inputs, VirtualKeyShort modifier)
    {
        // Most significant bit is set if key is down
        if ((GetAsyncKeyState((int)modifier) & 0x8000) != 0)
        {
            INPUT inputEvent = default(INPUT);
            inputEvent.Type = OSInterop.InputType.INPUT_KEYBOARD;
            inputEvent.U.Ki.WVk = modifier;
            inputEvent.U.Ki.DwFlags = KEYEVENTF.KEYUP;
            inputs.Add(inputEvent);
        }
    }

    internal static T OpenOrActivateWindow<T>() where T : Window, new()
    {
        WindowCollection allWindows = Application.Current.Windows;

        foreach (Window window in allWindows)
        {
            if (window is T matchWindow)
            {
                matchWindow.Activate();
                return matchWindow;
            }
        }

        // No Window Found, open a new one
        T newWindow = new();

        try
        {
            newWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred while trying to open a new window. Please try again.", ex.Message);
        }
        return newWindow;
    }

    public static void ShouldShutDown()
    {
        bool zeroOpenWindows = Application.Current.Windows.Count < 1;

        bool shouldShutDown = false;

        if (AppUtilities.TextGrabSettings.RunInTheBackground)
        {
            if (App.Current is App app)
            {
                if (app.NumberOfRunningInstances > 1
                    && app.TextGrabIcon == null
                    && zeroOpenWindows)
                    shouldShutDown = true;
            }
        }
        else if (zeroOpenWindows)
            shouldShutDown = true;

        if (shouldShutDown)
            Application.Current.Shutdown();
    }

    public static bool GetMousePosition(out Point mousePosition)
    {
        if (GetCursorPos(out POINT point))
        {
            mousePosition = new Point(point.X, point.Y);
            return true;
        }
        mousePosition = default;
        return false;
    }

    public static bool IsMouseInWindow(this Window window)
    {
        GetMousePosition(out Point mousePosition);

        DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        Point absPosPoint = window.GetAbsolutePosition();
        Rect windowRect = new(absPosPoint.X, absPosPoint.Y,
            window.ActualWidth * dpi.DpiScaleX,
            window.ActualHeight * dpi.DpiScaleY);
        return windowRect.Contains(mousePosition);
    }

    #region DLLImport

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    #endregion
}