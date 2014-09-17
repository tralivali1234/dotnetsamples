﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Runtime;
using System.IO;
using System.Xml;
using System.Linq;
using System.Text;
using System.IO.Compression;
using System.Collections;


static class Program
{

    public static void PrintDict(ClrRuntime runtime, string args)
    {
        bool failed = false;
        ulong obj = 0;
        try
        {
            obj = Convert.ToUInt64(args, 16);
            failed = obj == 0;
        }
        catch (ArgumentException)
        {
            failed = true;
        }

        if (failed)
        {
            Console.WriteLine("Usage: !PrintDict <dictionary>");
            return;
        }

        ClrHeap heap = runtime.GetHeap();
        ClrType type = heap.GetObjectType(obj);

        if (type == null)
        {
            Console.WriteLine("Invalid object {0:X}", obj);
            return;
        }

        if (!type.Name.StartsWith("System.Collections.Generic.Dictionary"))
        {
            Console.WriteLine("Error: Expected object {0:X} to be a dictionary, instead it's of type '{1}'.");
            return;
        }

        // Get the entries field.
        ulong entries = type.GetFieldValue(obj, "entries");

        if (entries == 0)
            return;

        ClrType entryArray = heap.GetObjectType(entries);
        ClrType arrayComponent = entryArray.ArrayComponentType;
        ClrInstanceField hashCodeField = arrayComponent.GetFieldByName("hashCode");
        ClrInstanceField keyField = arrayComponent.GetFieldByName("key");
        ClrInstanceField valueField = arrayComponent.GetFieldByName("value");

        Console.WriteLine("{0,8} {1,16} : {2}", "hash", "key", "value");
        int len = entryArray.GetArrayLength(entries);
        for (int i = 0; i < len; ++i)
        {
            ulong arrayElementAddr = entryArray.GetArrayElementAddress(entries, i);

            int hashCode = (int)hashCodeField.GetFieldValue(arrayElementAddr, true);
            object key = keyField.GetFieldValue(arrayElementAddr, true);
            object value = valueField.GetFieldValue(arrayElementAddr, true);

            key = Format(heap, key);
            value = Format(heap, value);

            bool skip = key is ulong && (ulong)key == 0 && value is ulong && (ulong)value == 0;

            if (!skip)
                Console.WriteLine("{0,8:X} {1,16} : {2}", hashCode, key, value);
        }
    }

    static object Format(ClrHeap heap, object val)
    {
        if (val == null)
            return "{null}";

        if (val is ulong)
        {
            ulong addr = (ulong)val;
            var type = heap.GetObjectType(addr);
            if (type != null && type.Name == "System.String")
                return type.GetValue(addr);
            else
                return ((ulong)val).ToString("X");
        }

        return val;
    }

    static ulong GetFieldValue(this ClrType type, ulong obj, string name)
    {
        var field = type.GetFieldByName(name);
        if (field == null)
            return 0;

        object val = field.GetFieldValue(obj);
        if (val is ulong)
            return (ulong)val;

        return 0;
    }

    static void Main(string[] args)
    {
        //ClrRuntime runtime = CreateRuntime(@"C:\Users\leculver\Desktop\work\projects\rmd_test_data\dumps\v4.0.30319.239_x86.cab",
        //                                   @"C:\Users\leculver\Desktop\work\projects\rmd_test_data\dacs");
        //PrintDict(runtime, "0262b058");

        List<string> typeNames = new List<string>();
        ClrRuntime runtime = CreateRuntime(@"D:\work\03_09_ml\ml.dmp", @"D:\work\03_09_ml");
        ClrHeap heap = runtime.GetHeap();
        foreach (var type in heap.EnumerateTypes())
        {
            typeNames.Add(type.Name);
        }

        typeNames.Sort(delegate(string a, string b) { return -a.Length.CompareTo(b.Length); });

        for (int i = 0; i < 10; ++i)
            Console.WriteLine("{0} {1}", typeNames[i].Length, typeNames[i]);
    }


    private static ClrRuntime CreateRuntime(string dump, string dac)
    {
        // Create the data target.  This tells us the versions of CLR loaded in the target process.
        DataTarget dataTarget = DataTarget.LoadCrashDump(dump);

        // Now check bitness of our program/target:
        bool isTarget64Bit = dataTarget.PointerSize == 8;
        if (Environment.Is64BitProcess != isTarget64Bit)
            throw new Exception(string.Format("Architecture mismatch:  Process is {0} but target is {1}", Environment.Is64BitProcess ? "64 bit" : "32 bit", isTarget64Bit ? "64 bit" : "32 bit"));

        // Note I just take the first version of CLR in the process.  You can loop over every loaded
        // CLR to handle the SxS case where both v2 and v4 are loaded in the process.
        var version = dataTarget.ClrVersions[0];

        // Next, let's try to make sure we have the right Dac to load.  CLRVersionInfo will actually
        // have the full path to the right dac if you are debugging the a version of CLR you have installed.
        // If they gave us a path (and not the actual filename of the dac), we'll try to handle that case too:
        if (dac != null && Directory.Exists(dac))
            dac = Path.Combine(dac, version.DacInfo.FileName);
        else if (dac == null || !File.Exists(dac))
            dac = version.TryGetDacLocation();

        // Finally, check to see if the dac exists.  If not, throw an exception.
        if (dac == null || !File.Exists(dac))
            throw new FileNotFoundException("Could not find the specified dac.", dac);

        // Now that we have the DataTarget, the version of CLR, and the right dac, we create and return a
        // ClrRuntime instance.
        return dataTarget.CreateRuntime(dac);
    }
}

