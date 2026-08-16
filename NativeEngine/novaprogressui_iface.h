#ifndef NOVAPROGRESSUI_IFACE_H
#define NOVAPROGRESSUI_IFACE_H

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <oleauto.h>

/*
 * NovaSCM usa IDispatch (OLE Automation) invece di un'interfaccia COM
 * custom a vtable: IDispatch ha marshalling FORNITO DAL SISTEMA
 * (oleaut32.dll / StdMarshal), quindi funziona cross-process (motore e GUI
 * sono due .exe separati) senza bisogno di un proxy/stub compilato da IDL
 * (che richiederebbe MIDL, non disponibile in questo toolchain).
 *
 * DISPID concordati tra client (NovaTsManager) e server (NovaTsProgressUI):
 *   1 = ShowStep(stepIdx:I4, stepCount:I4, label:BSTR, actionPct:I4, totalPct:I4, details:BSTR)
 *   2 = ShowError(message:BSTR)
 *   3 = ShowDone()
 */
#define DISPID_SHOWSTEP  1
#define DISPID_SHOWERROR 2
#define DISPID_SHOWDONE  3

DEFINE_GUID(CLSID_NovaProgressUI,
    0x4e6f7661, 0x5473, 0x5049, 0x92, 0x01, 0x00, 0x11, 0x22, 0x33, 0x44, 0x01);

#endif
