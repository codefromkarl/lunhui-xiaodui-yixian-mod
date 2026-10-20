#!/usr/bin/env python3
"""User-authorized X11 game-only UI test driver. No save or process manipulation."""
import ctypes as C, ctypes.util, argparse, time, json
from pathlib import Path
from PIL import Image
X=C.CDLL(ctypes.util.find_library('X11')); T=C.CDLL(ctypes.util.find_library('Xtst'))
P=C.c_void_p; U=C.c_ulong; I=C.c_int
X.XOpenDisplay.argtypes=[C.c_char_p];X.XOpenDisplay.restype=P
X.XGetImage.argtypes=[P,U,I,I,C.c_uint,C.c_uint,U,I];X.XGetImage.restype=P
X.XDestroyImage.argtypes=[P]
X.XGetGeometry.argtypes=[P,U,C.POINTER(U),C.POINTER(I),C.POINTER(I),C.POINTER(C.c_uint),C.POINTER(C.c_uint),C.POINTER(C.c_uint),C.POINTER(C.c_uint)]
X.XTranslateCoordinates.argtypes=[P,U,U,I,I,C.POINTER(I),C.POINTER(I),C.POINTER(U)]
X.XDefaultRootWindow.argtypes=[P];X.XDefaultRootWindow.restype=U
X.XFlush.argtypes=[P];X.XSync.argtypes=[P,I]
X.XFetchName.argtypes=[P,U,C.POINTER(C.c_char_p)]
X.XFree.argtypes=[P]
X.XGetInputFocus.argtypes=[P,C.POINTER(U),C.POINTER(I)]
X.XSetInputFocus.argtypes=[P,U,I,U]
X.XRaiseWindow.argtypes=[P,U]
X.XKeysymToKeycode.argtypes=[P,U];X.XKeysymToKeycode.restype=C.c_ubyte
X.XStringToKeysym.argtypes=[C.c_char_p];X.XStringToKeysym.restype=U
T.XTestFakeMotionEvent.argtypes=[P,I,I,I,U]
T.XTestFakeButtonEvent.argtypes=[P,C.c_uint,I,U]
T.XTestFakeKeyEvent.argtypes=[P,C.c_uint,I,U]
class XI(C.Structure):
 _fields_=[('width',I),('height',I),('xoffset',I),('format',I),('data',P),('byte_order',I),('bitmap_unit',I),('bitmap_bit_order',I),('bitmap_pad',I),('depth',I),('bytes_per_line',I),('bits_per_pixel',I),('red_mask',U),('green_mask',U),('blue_mask',U),('obdata',P)]
errors=[]
@C.CFUNCTYPE(I,P,P)
def error(d,e):errors.append('X11 request error');return 0
X.XSetErrorHandler.argtypes=[P];X.XSetErrorHandler(error)
d=X.XOpenDisplay(b':0')
if not d:raise RuntimeError('Cannot open X11 display')
import subprocess,re
windows=subprocess.check_output(["xwininfo","-root","-tree"],env={**__import__("os").environ,"DISPLAY":":0"},text=True)
matches=re.findall(r'(0x[0-9a-f]+) "轮回小队":.*steam_app_5241190',windows)
if len(matches)!=1:raise RuntimeError("Expected exactly one game window")
w=int(matches[0],16)
identity=subprocess.check_output(['xprop','-id',hex(w),'WM_CLASS','_NET_WM_NAME'],env={**__import__('os').environ,'DISPLAY':':0'},text=True)
if 'steam_app_5241190' not in identity or '轮回小队' not in identity:raise RuntimeError('Game window changed: '+identity)
root=U();xx=I();yy=I();width=C.c_uint();height=C.c_uint();border=C.c_uint();depth=C.c_uint()
if not X.XGetGeometry(d,w,C.byref(root),C.byref(xx),C.byref(yy),C.byref(width),C.byref(height),C.byref(border),C.byref(depth)):raise RuntimeError('Game window unavailable')
parser=argparse.ArgumentParser();parser.add_argument('action',choices=['shot','click','key','scroll','drag']);parser.add_argument('args',nargs='*');a=parser.parse_args()
out=Path('logs/yixian-ui-test');out.mkdir(exist_ok=True)
def pos(x,y):
 x=int(x);y=int(y)
 if not(0<=x<width.value and 0<=y<height.value):raise ValueError('Coordinate outside game')
 ax=I();ay=I();child=U();X.XTranslateCoordinates(d,w,X.XDefaultRootWindow(d),x,y,C.byref(ax),C.byref(ay),C.byref(child));return ax.value,ay.value
if a.action=='shot':
 image=X.XGetImage(d,w,0,0,width.value,height.value,2**64-1,2)
 if not image:raise RuntimeError('Game window capture failed: '+str(errors))
 info=C.cast(image,C.POINTER(XI)).contents
 print('XImage',info.width,info.height,info.bits_per_pixel,hex(info.red_mask))
 raw=C.string_at(info.data,info.bytes_per_line*info.height)
 im=Image.frombytes('RGB',(info.width,info.height),raw,'raw','BGRX',info.bytes_per_line,1)
 path=out/(a.args[0] if a.args else time.strftime('%H%M%S')+'.png');im.save(path);X.XDestroyImage(image);print(path)
else:
 # Ask the window manager to activate the game before delivering XTest input.
 class ClientData(C.Union):_fields_=[('b',C.c_char*20),('s',C.c_short*10),('l',C.c_long*5)]
 class Client(C.Structure):_fields_=[('type',I),('serial',U),('send_event',I),('display',P),('window',U),('message_type',U),('format',I),('data',ClientData)]
 X.XInternAtom.argtypes=[P,C.c_char_p,I];X.XInternAtom.restype=U
 X.XSendEvent.argtypes=[P,U,I,C.c_long,P]
 event=Client();event.type=33;event.display=d;event.window=w;event.message_type=X.XInternAtom(d,b'_NET_ACTIVE_WINDOW',0);event.format=32;event.data.l[0]=2;event.data.l[1]=0
 X.XSendEvent(d,X.XDefaultRootWindow(d),0,(1<<20)|(1<<19),C.byref(event));X.XFlush(d);time.sleep(.2)
 X.XRaiseWindow(d,w);X.XSetInputFocus(d,w,2,0);X.XSync(d,0)
 focus=U();revert=I();X.XGetInputFocus(d,C.byref(focus),C.byref(revert))
 if focus.value!=w:raise RuntimeError('Game did not receive focus; not sending input')
 if a.action in ['click','scroll','drag']:
  x,y=pos(*a.args[:2]);X.XWarpPointer.argtypes=[P,U,U,I,I,C.c_uint,C.c_uint,I,I]
  X.XWarpPointer(d,0,w,0,0,0,0,int(a.args[0]),int(a.args[1]));X.XFlush(d);time.sleep(.12)
 if a.action=='click':
  class ButtonEvent(C.Structure):
   _fields_=[('type',I),('serial',U),('send_event',I),('display',P),('window',U),('root',U),('subwindow',U),('time',U),('x',I),('y',I),('x_root',I),('y_root',I),('state',C.c_uint),('button',C.c_uint),('same_screen',I)]
  e=ButtonEvent();e.display=d;e.window=w;e.root=root.value;e.same_screen=1;e.x=int(a.args[0]);e.y=int(a.args[1]);e.x_root=x;e.y_root=y;e.button=int(a.args[2]) if len(a.args)>2 else 1
  e.type=4;X.XSendEvent(d,w,1,4,C.byref(e));X.XFlush(d);time.sleep(.16)
  e.type=5;e.state=1<<(7+e.button);X.XSendEvent(d,w,1,8,C.byref(e))
 elif a.action=='key':
  class KeyEvent(C.Structure):
   _fields_=[('type',I),('serial',U),('send_event',I),('display',P),('window',U),('root',U),('subwindow',U),('time',U),('x',I),('y',I),('x_root',I),('y_root',I),('state',C.c_uint),('keycode',C.c_uint),('same_screen',I)]
  e=KeyEvent();e.display=d;e.window=w;e.root=root.value;e.same_screen=1;e.keycode=X.XKeysymToKeycode(d,X.XStringToKeysym(a.args[0].encode()))
  e.type=2;X.XSendEvent(d,w,1,1,C.byref(e));X.XFlush(d);time.sleep(.16)
  e.type=3;X.XSendEvent(d,w,1,2,C.byref(e))
 elif a.action=='scroll':
  for _ in range(abs(int(a.args[2]))):
   b=5 if int(a.args[2])>0 else 4;T.XTestFakeButtonEvent(d,b,1,0);T.XTestFakeButtonEvent(d,b,0,0);X.XFlush(d);time.sleep(.08)
 elif a.action=='drag':
  end=pos(*a.args[2:4]);T.XTestFakeButtonEvent(d,1,1,0);X.XFlush(d)
  for i in range(1,16):T.XTestFakeMotionEvent(d,-1,x+(end[0]-x)*i//15,y+(end[1]-y)*i//15,0);X.XFlush(d);time.sleep(.025)
  T.XTestFakeButtonEvent(d,1,0,0)
 X.XSync(d,0)
 with (out/'actions.jsonl').open('a') as f:f.write(json.dumps(dict(time=time.strftime('%FT%T'),action=a.action,args=a.args,window=hex(w)),ensure_ascii=False)+'\n')
 print('sent',a.action,a.args,'errors',errors)
