"""Reads the plan library (Assets/Scripts/Gen/GenPlans.cs) for the prototype."""
import os, re, collections
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Scripts", "Gen", "GenPlans.cs")
def load():
    t=open(SRC,encoding="utf8").read()
    def arr(name):
        m=re.search(name+r"\s*=\s*\{(.*?)\};",t,re.S)
        body=re.sub(r"//[^\n]*","",m.group(1))
        return [int(x) for x in re.findall(r"-?\d+",body)]
    start=arr("Start"); data=arr("Data")
    plans=[]
    for s in start:
        z,w,d,nr=data[s:s+4]
        roles=data[s+4:s+4+nr]
        cells=data[s+4+nr:s+4+nr+w*d]
        db=s+4+nr+w*d
        nd=data[db]; doors=data[db+1:db+1+nd]; front=data[db+1+nd]; back=data[db+2+nd]
        plans.append(dict(zone=z,w=w,d=d,roles=roles,cells=cells,doors=doors,front=front,back=back,n=sum(1 for c in cells if c>=0)))
    return plans
if __name__=="__main__":
    P=load(); print(len(P))
    c=collections.Counter((p["zone"],p["w"],p["d"]) for p in P)
    for k in sorted(c): print(k,c[k])
