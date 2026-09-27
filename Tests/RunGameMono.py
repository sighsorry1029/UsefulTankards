"""Run an isolated managed test executable using a game's existing Mono runtime.

Usage: RunGameMono.py <game root> <Managed directory> <test.exe> [test arguments...]
No Unity scene, BepInEx plugin initialization, or running game process is involved.
"""
import ctypes
import os
from pathlib import Path
import sys

game_root, managed, executable = (Path(value).resolve() for value in sys.argv[1:4])
mono_root = game_root / "MonoBleedingEdge"
mono = ctypes.CDLL(str(mono_root / "EmbedRuntime" / "mono-2.0-bdwgc.dll"))
mono.mono_set_dirs.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
mono.mono_set_assemblies_path.argtypes = [ctypes.c_char_p]
mono.mono_jit_init_version.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
mono.mono_jit_init_version.restype = ctypes.c_void_p
mono.mono_domain_assembly_open.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
mono.mono_domain_assembly_open.restype = ctypes.c_void_p
mono.mono_jit_exec.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_char_p)]
mono.mono_jit_exec.restype = ctypes.c_int
mono.mono_set_dirs(str(managed).encode(), str(mono_root / "etc").encode())
mono.mono_set_assemblies_path(str(managed).encode())
if os.environ.get("USEFULTANKARDS_MONO_OPTIONS"):
    options = os.environ["USEFULTANKARDS_MONO_OPTIONS"].split()
    mono.mono_jit_parse_options.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_char_p)]
    option_argv = (ctypes.c_char_p * len(options))(*(value.encode() for value in options))
    mono.mono_jit_parse_options(len(options), option_argv)
domain = mono.mono_jit_init_version(b"UsefulTankards.Regression", b"v4.0.30319")
if not domain:
    raise RuntimeError("Mono initialization failed")
assembly = mono.mono_domain_assembly_open(domain, str(executable).encode())
if not assembly:
    raise RuntimeError("Test assembly could not be loaded")
arguments = [str(executable), str(managed), *sys.argv[4:]]
argv = (ctypes.c_char_p * len(arguments))(*(value.encode() for value in arguments))
result = mono.mono_jit_exec(domain, assembly, len(arguments), argv)
sys.stdout.flush()
sys.stderr.flush()
os._exit(result)
