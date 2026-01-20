#!/usr/bin/env dotnet-script
// Run with: dotnet script test_mouse.cs
// Or compile: dotnet build and run

using System;
using System.IO;
using System.Threading;

class MouseTest
{
    const ushort EV_REL = 0x02;
    const ushort REL_X = 0x00;
    const ushort REL_Y = 0x01;

    static void Main()
    {
        Console.WriteLine("=== KAMI Mouse Input Test ===\n");
        
        // Find mouse device
        string? mouseDevice = null;
        for (int i = 0; i < 20; i++)
        {
            string device = $"/dev/input/event{i}";
            if (!File.Exists(device)) continue;
            
            string eventName = $"event{i}";
            string namePath = $"/sys/class/input/{eventName}/device/name";
            string capsPath = $"/sys/class/input/{eventName}/device/capabilities/rel";
            
            if (!File.Exists(namePath)) continue;
            
            string name = File.ReadAllText(namePath).Trim();
            Console.WriteLine($"[{eventName}] {name}");
            
            if (File.Exists(capsPath))
            {
                string caps = File.ReadAllText(capsPath).Trim();
                if (long.TryParse(caps, System.Globalization.NumberStyles.HexNumber, null, out long capValue))
                {
                    if ((capValue & 0x3) == 0x3) // Has REL_X and REL_Y
                    {
                        Console.WriteLine($"  ^ This has mouse capabilities (rel caps: {caps})");
                        if (mouseDevice == null && 
                            (name.ToLower().Contains("mouse") || 
                             name.ToLower().Contains("pointer") ||
                             name.ToLower().Contains("touchpad")))
                        {
                            mouseDevice = device;
                            Console.WriteLine($"  ^ Selected as mouse device!\n");
                        }
                    }
                }
            }
        }
        
        if (mouseDevice == null)
        {
            // Fallback - pick first with capabilities
            for (int i = 0; i < 20; i++)
            {
                string device = $"/dev/input/event{i}";
                string capsPath = $"/sys/class/input/event{i}/device/capabilities/rel";
                if (File.Exists(capsPath))
                {
                    string caps = File.ReadAllText(capsPath).Trim();
                    if (long.TryParse(caps, System.Globalization.NumberStyles.HexNumber, null, out long capValue))
                    {
                        if ((capValue & 0x3) == 0x3)
                        {
                            mouseDevice = device;
                            break;
                        }
                    }
                }
            }
        }
        
        if (mouseDevice == null)
        {
            Console.WriteLine("\nERROR: No mouse device found!");
            Console.WriteLine("Make sure you're in the 'input' group: sudo usermod -aG input $USER");
            return;
        }
        
        Console.WriteLine($"\nOpening {mouseDevice}...");
        Console.WriteLine("Move your mouse. You should see X/Y values changing.");
        Console.WriteLine("Press Ctrl+C to exit.\n");
        
        try
        {
            using var fs = new FileStream(mouseDevice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buffer = new byte[24];
            int totalX = 0, totalY = 0;
            
            while (true)
            {
                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                if (bytesRead == 24)
                {
                    ushort type = BitConverter.ToUInt16(buffer, 16);
                    ushort code = BitConverter.ToUInt16(buffer, 18);
                    int value = BitConverter.ToInt32(buffer, 20);
                    
                    if (type == EV_REL)
                    {
                        if (code == REL_X) totalX += value;
                        if (code == REL_Y) totalY += value;
                        Console.Write($"\rMouse delta: X={totalX,6}  Y={totalY,6}  (move={value})    ");
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine($"\nERROR: Permission denied for {mouseDevice}");
            Console.WriteLine("Add yourself to 'input' group: sudo usermod -aG input $USER");
            Console.WriteLine("Then log out and back in.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
        }
    }
}

MouseTest.Main();
