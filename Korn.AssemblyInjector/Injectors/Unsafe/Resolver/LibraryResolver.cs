﻿using Korn.Shared;
using Korn.Utils;
using Korn.Utils.PEImageReader;
using System.Net;
using System.Reflection.PortableExecutable;

namespace Korn.AssemblyInjector;
internal unsafe class LibraryResolver : IDisposable
{
    public LibraryResolver(string moduleName, nint processHandle) : this(moduleName, processHandle, new(processHandle)) { }

    public LibraryResolver(string moduleName, nint processHandle, ProcessModulesResolver modulesResolver)
    {
        ProcessHandle = processHandle;
        ModulesResolver = modulesResolver;
        var module = ModulesResolver.ResolveModule(moduleName);
        if (module is null)
            throw new KornError([
                "UnsafeInjector.CoreClrResolver->.ctor(nint, ProcessModulesResolver):",
                $"CoreClr module not found in target process"
            ]);

        ModuleHandle = module.ModuleHandle;
        PEImage = new PEImage(module.Path);

        var debugSymbolsPath = ResolveDebugSymbols(module.Path);
        PdbResolver = new PdbResolver(debugSymbolsPath);
    }

    public readonly nint ProcessHandle;
    public readonly ProcessModulesResolver ModulesResolver;
    public readonly PdbResolver PdbResolver;
    public readonly PEImage PEImage;
    public readonly nint ModuleHandle;

    string ResolveDebugSymbols(string modulePath)
    {
        var debugSymbolsPath = PdbResolver.GetDebugSymbolsPathForExecutable(modulePath);
        if (!File.Exists(debugSymbolsPath))
            DownloadDebugSymbols(modulePath, debugSymbolsPath);

        return debugSymbolsPath;
    }

    void DownloadDebugSymbols(string modulePath, string debugSymbolsPath)
    {
        var pdbPath = PdbResolver.GetDebugSymbolsPathForExecutable(modulePath);
        var debugInfo = PEImage.ReadDegubInfo();
        if (debugInfo is null)
            throw new KornError([
                $"UnsafeInjector.LibraryResolver({GetType().Name})->DownloadDebugSymbols:",
                $"Failed to get module's debug informationю"
            ]);

        var downloadUrl = debugInfo.GetMicrosoftDebugSymbolsCacheUrl();
        try
        {
            KornShared.Logger.WriteMessage($"Start downloading debug symbols from microsoft server for a file \"{modulePath}\" from ulr \"{downloadUrl}\"");
            new WebClient().DownloadFile(downloadUrl, debugSymbolsPath);
        }
        catch (Exception ex)
        {
            throw new KornException($"UnsafeInjector.LibraryResolver({GetType().Name})->DownloadDebugSymbols: Exception thrown when downloading debug symbols from microsoft server", ex);
        }
    }

    private protected nint Resolve(string name, string declaringType)
    {
        var symbol = PdbResolver.Resolve(name, declaringType);
        var sector = PEImage.GetSectionByNumber(symbol->Segment);
        var offset = sector->VirtualAddress + symbol->SegmentOffset;
        return (nint)(ModuleHandle + offset);
    }

    private protected nint Resolve(string name)
    {
        var symbol = PdbResolver.Resolve(name);
        return (nint)(ModuleHandle + 0x1000/*header size*/ + symbol->HeaderOffset);
    }

    bool disposed;
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        PdbResolver.Dispose();
        PEImage.Dispose();
    }

    ~LibraryResolver() => Dispose();
}