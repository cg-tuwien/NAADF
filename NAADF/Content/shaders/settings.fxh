#ifndef __SETTINGS__
#define __SETTINGS__


// BUILD FLAGS START
// NOTE: Make sure they are the same as in "Settings.cs"

#define ENTITIES
//#define HDR // Note that HDR requires fullscreen

// BUILD FLAGS END


#ifdef ENTITIES
#define CHUNKTYPE uint2
#else
#define CHUNKTYPE uint
#endif

#endif // __SETTINGS__