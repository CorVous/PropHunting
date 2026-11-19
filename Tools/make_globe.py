import math, sys
from PIL import Image, ImageDraw, ImageFilter
S=4; N=256*S
img=Image.new("RGBA",(N,N),(0,0,0,0))
cx,cy,r=N*0.5,N*0.47,N*0.36
# sphere shading: per-pixel lambert with a light from upper left
px=img.load()
land=Image.new("L",(N,N),0); d=ImageDraw.Draw(land)
for (x,y,a,b,rot) in [(0.40,0.36,0.13,0.09,20),(0.47,0.55,0.07,0.12,-15),(0.64,0.40,0.10,0.07,10),(0.70,0.62,0.06,0.05,0),(0.33,0.60,0.05,0.04,0)]:
    blob=Image.new("L",(N,N),0); ImageDraw.Draw(blob).ellipse([(x-a)*N,(y-b)*N,(x+a)*N,(y+b)*N],fill=255)
    land.paste(255,(0,0),blob.rotate(rot,center=(x*N,y*N)))
lp=land.load()
L=(-0.45,-0.55,0.70); ln=math.sqrt(sum(v*v for v in L)); L=tuple(v/ln for v in L)
for j in range(int(cy-r)-2,int(cy+r)+3):
    for i in range(int(cx-r)-2,int(cx+r)+3):
        dx,dy=(i-cx)/r,(j-cy)/r; q=dx*dx+dy*dy
        if q>1: continue
        dz=math.sqrt(1-q); lam=max(0,dx*L[0]+dy*L[1]+dz*L[2])
        k=0.45+0.6*lam
        base=(74,160,222) if lp[i,j]==0 else (126,196,104)
        spec=max(0,lam)**30*90
        px[i,j]=tuple(min(255,int(c*k+spec)) for c in base)+(255,)
ov=Image.new('RGBA',(N,N),(0,0,0,0)); d=ImageDraw.Draw(ov)
# meridian / equator lines
for t in (-0.5,0,0.5):
    w=abs(math.cos(t*math.pi/2))*r*0.0+r*abs(t)
for k in (0.35,0.7):
    d.ellipse([cx-r*k,cy-r,cx+r*k,cy+r],outline=(255,255,255,60),width=3*S)
d.line([cx-r,cy,cx+r,cy],fill=(255,255,255,60),width=3*S)
img=Image.alpha_composite(img,ov); d=ImageDraw.Draw(img)
d.ellipse([cx-r,cy-r,cx+r,cy+r],outline=(40,90,140,255),width=4*S)
# stand: a wooden arc and foot
d.arc([cx-r*1.18,cy-r*1.18,cx+r*1.18,cy+r*1.18],60,300,fill=(190,140,70,255),width=9*S) if False else None
d.rectangle([cx-6*S,cy+r,cx+6*S,cy+r+18*S],fill=(150,105,50,255))
d.ellipse([cx-46*S,cy+r+12*S,cx+46*S,cy+r+34*S],fill=(176,124,62,255))
img=img.resize((256,256),Image.LANCZOS)
img.save(sys.argv[1])
