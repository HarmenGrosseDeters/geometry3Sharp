# bits B of the smallest common power-of-two grid holding all coordinates of a mesh: |x| = m * 2^e, B = max bit length of m
import struct, sys, os, math, statistics as st
def coords(path):
    data=open(path,'rb').read()
    if len(data)>=84:
        n=struct.unpack_from('<I',data,80)[0]
        if 84+50*n==len(data):
            vals=[]
            for i in range(n):
                f=struct.unpack_from('<12f',data,84+50*i)
                vals+=f[3:12]
            return 'bin',vals
    vals=[]
    for line in data.decode('latin1').splitlines():
        t=line.split()
        if t and t[0]=='vertex': vals+=[float(x) for x in t[1:4]]
    return 'ascii',vals
def grid(vals):
    lo=None; hi=None
    for x in vals:
        if x==0: continue
        m,e=math.frexp(abs(x))          # |x| = m 2^e, m in [0.5,1)
        i=int(m*2**53); tz=(i & -i).bit_length()-1
        low=e-53+tz; hi=e if hi is None else max(hi,e); lo=low if lo is None else min(lo,low)
    return hi-lo, lo, hi
rows=[]
for f in sorted(os.listdir(sys.argv[1])):
    k,v=coords(os.path.join(sys.argv[1],f))
    if not v: continue
    B,lo,hi=grid(v)
    # quantile grid: B if the 0.1 % finest coordinates were dropped (how much a few values cost)
    rows.append((f.split('.')[0],k,B,hi))
    print(f.split('.')[0],k,B,hi,flush=True)
