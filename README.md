videocore-elf-dis
=================

experimental ELF disassembler for the Videocore IV processor.

Builds with monodevelop 3.0, .Net 4.0

Based on original implementation:
https://github.com/hermanhermitage/videocoreiv
https://github.com/hermanhermitage/videocore-disjs
See README's for additional license/disclaimer info.

Usage:

vcelfdis.exe FILE_obj >FILE.asm

Includes some modifications to videocoreiv.arch for experiments I was doing with vector decoding, but these are not even remotely accurate!!

No license; consider this released into the public domain unless disallowed by the base project(s) - see above.

Uses NCalc library (http://ncalc.codeplex.com/); available under MIT License.
