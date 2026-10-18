using System.Text;
using System.Text.Json;

namespace JadeView;

// Typed PC FF0 traversal. Keys come from dependency requests, never numeric proximity.
// Handler layouts were checked against Iris 0.26 and the original game's callbacks.
internal sealed class PcSceneLoader
{
    internal sealed record Request(uint Key, char Kind, bool Cache = true);
    internal sealed class Record(uint key, char kind, int offset, int size)
    {
        public uint Key = key; public char Kind = kind; public int Offset = offset, Size = size;
        public uint Parent = uint.MaxValue, Prefab = uint.MaxValue;
        public uint Flags, Geometry, Material, Group, Events = uint.MaxValue, Actions = uint.MaxValue,
            Shape = uint.MaxValue, Skeleton = uint.MaxValue;
        public string Name = "";
        public List<Request> References = [];
        public List<Record> WorldMembers = [];
        public List<(uint Type,int Offset,int Size)> Modifiers = [];
        public Dictionary<uint,Record> ActorResources = [];
    }
    internal sealed class Result
    {
        public List<Record> Records = []; public List<Record> Worlds = [];
        public Dictionary<uint, Record> Resources = [];
        public int End, SpeSkipped, CachedSkipped;
        public string? Error;
        public bool Complete => Error == null;
    }
    private readonly byte[] b;
    private readonly HashSet<uint> spe;
    private readonly Queue<Request> queue = new();
    private readonly Dictionary<uint, Record> cache = [];
    private readonly HashSet<uint> shapeKeys = [];
    private readonly Result result = new();
    private int cursor, budget;
    private bool fixedResources;
    private Record? current;
    private PcSceneLoader(byte[] data, HashSet<uint> special) { b = data; spe = special; }
    private uint U(int p) { if (p < 0 || p > b.Length - 4) throw new InvalidDataException($"FF0 cursor {p:X8} outside stream."); return Preview.U32(b, p); }
    private int I(int p) => checked((int)U(p));
    private ushort S(int p) { if(p < 0 || p > b.Length-2) throw new InvalidDataException("Truncated FF0 word."); return (ushort)Preview.U16(b,p); }
    private string Text(int p,int n) { if(p<0 || n<0 || p>b.Length-n) throw new InvalidDataException("Truncated FF0 string."); return Encoding.Latin1.GetString(b,p,n).Split('\0')[0].Trim(); }
    private void Add(uint key, char kind, bool keep = true, bool allowZero = false)
    {
        if (key == uint.MaxValue || (!allowZero && key == 0)) return;
        var request = new Request(key,kind,keep); queue.Enqueue(request); current?.References.Add(request);
    }
    private void Drain()
    {
        while(queue.TryDequeue(out var request))
        {
            if(++budget>1000000) throw new InvalidDataException("FF0 dependency budget exceeded.");
            if(!fixedResources && spe.Contains(request.Key)) { result.SpeSkipped++; continue; }
            if(cache.ContainsKey(request.Key)) { result.CachedSkipped++; continue; }
            int start=cursor;
            if(request.Kind=='u')
            {
                string tag=Text(start,4);
                int shapeSize=I(start+4);cursor=checked(start+8+shapeSize);
                if(tag==".snk"){int listSize=I(cursor);cursor=checked(cursor+4+listSize);}
                if(cursor>b.Length)throw new InvalidDataException("Shape outside stream.");
                var shape=new Record(request.Key,'u',start+8,cursor-start-8);result.Records.Add(shape);cache[request.Key]=shape;result.Resources[request.Key]=shape;continue;
            }
            int size=I(start), p=start+4;
            if(size<0 || size>b.Length-p) throw new InvalidDataException($"Bad {request.Kind} size at {start:X8}, key {request.Key:X8}.");
            var record=new Record(request.Key,request.Kind,p,size); current=record;
            result.Records.Add(record); Parse(record);
            cursor=checked(p+record.Size); current=null;
            if(request.Cache) cache[request.Key]=record;
            result.Resources[request.Key]=record;
        }
    }
    private void Parse(Record r)
    {
        int p=r.Offset,end=p+r.Size;
        void Ref(int offset,char kind,bool keep=true) => Add(U(p+offset),kind,keep);
        switch(r.Kind)
        {
            case 'z':
                if(Text(p,4)!=".wow" || U(p+4)!=3 || r.Size<180) throw new InvalidDataException("Invalid WOW.");
                r.Name=Text(p+16,60); Ref(160,'o'); Ref(164,'o');r.Group=U(p+168);Ref(168,'n',false);Ref(172,'v');result.Worlds.Add(r);break;
            case 'n': case 'j':
                if(r.Size%4!=0) throw new InvalidDataException("Invalid GAO key list.");
                for(int q=p;q<end;q+=4)Add(U(q),'l');break;
            case 'q': Ref(0,'s');Ref(4,'t');break;
            case 's':
                for(int q=p;q+8<=end;q+=8) { string tag=Text(q+4,4);if(tag==".ova")Add(U(q),'t');else if(tag==".ofc")Add(U(q),'r'); }break;
            case 'k': Ref(0,'j');break;
            case 'd': Ref(0,'e');break;
            case 'f':
                if(r.Size==4)Ref(0,'g');else for(int i=0;i<b[p];i++)Ref(4+i*4,'g');break;
            case 'g': if(b[p+4] is 1 or 2 or 3 or 5)Ref(0,'h');break;
            case 'o':Ref(0,'p');break;
            case 'v':for(int i=0;i<I(p);i++)Ref(4+i*4,'x');break;
            case 'x':Ref(0,'l');break;
            case 'w':
                int w=p+4;for(int i=0;i<I(p);i++) { Add(U(w),'x');int n=I(w+4);w+=8;for(int j=0;j<n;j++,w+=9)Add(U(w+2),'l'); }break;
            case 'b':
                for(int q=p;q<end;q+=4) {uint key=U(q);if(key==1){q+=4;continue;}Add(key,'a');}break;
            case 'a':
                if(r.Size>0 && b[p]>0)for(int i=0;i<b[p];i++) {int q=p+2+i*19;Add(U(q),'i');Add(U(q+8),'c');}break;
            case 't':Variables(p,end);break;
            case 'i':Animation(p,end);break;
            case 'l':Gao(r);break;
            case 'm':
                int type=I(p);
                if(type==4)for(int i=0;i<I(p+4);i++)Ref(8+i*4,'m');
                else if(type==8)for(int i=0;i<Math.Min(6,(int)b[p+4]);i++)Ref(11+i*4,'m');
                else if(type==2)Ref(28,'l');
                else if(type is not (0 or 1 or 3 or 5 or 6 or 10 or 11))throw new InvalidDataException($"Unsupported graphic type {type} at {p:X8}.");break;
            case 'c': case 'e': case 'h': case 'p': case 'r': break; // Framed leaves: no FF0 dependency callbacks.
            default:throw new InvalidDataException($"Unsupported FF0 handler {r.Kind}.");
        }
    }
    private void Variables(int p,int end)
    {
        if(p==end)return;
        int bytes=I(p),count=bytes/12, q=checked(p+4+bytes);int[] lengths=new int[count];ushort[] types=new ushort[count];
        for(int i=0;i<count;i++){lengths[i]=unchecked((int)U(p+4+i*12+4));types[i]=S(p+4+i*12+8);}
        q=checked(q+4+count*30);int present=I(q);q+=4;
        if(present!=0)for(int i=0;i<count;i++)
        {
            uint n=(uint)lengths[i];if(n!=1)q+=checked((int)(n>>30)*4);n&=0x3fffffff;
            int stride=types[i] switch {37=>12,41=>52,44=>8,32 or 33 or 34 or 38 or 39 or 40 or 42 or 43 or 45 or 46 or 132=>4,_=>0};
            q=checked(q+(int)n*stride);
        }
        for(int i=0;i<5;i++,q+=4)Add(U(q),'r');
        if(q<end){int n=I(q);q+=4;for(int i=0;i<n;i++,q+=4)Add(U(q),'r');}
        if(q!=end)throw new InvalidDataException($"OVA layout mismatch at {p:X8}: {q:X8}/{end:X8}.");
    }
    private void Animation(int p,int end)
    {
        int q=p+4,previousSize=0,previousFormat=0;
        for(int channel=0;channel<S(p);channel++)
        {
            int flags=S(q), mode=flags&0x3f00;q+=8;
            if((flags&0x8000)!=0){if(mode==0)q+=4;}
            else{Add(U(q),'l');q+=4;}
            int count=I(q);q+=4;int firstFlags=0;
            for(int frame=0;frame<count;frame++)
            {
                q+=(mode&512)!=0?1:2;
                int f;if((mode&2048)!=0 && frame>0)f=firstFlags&~4;else{f=S(q);q+=2;if(frame==0)firstFlags=f;}
                f&=~2;
                if((f&1984)==64){int n=I(q);q=checked(q+4+n*8);continue;}
                if((f&1984)!=128)continue;
                if((f&32)!=0){q+=4;continue;}
                int controls=frame>0?mode:((mode&8192)!=0?256:0),start=q;
                int bytes;if((controls&8192)!=0)bytes=previousSize/2;else{bytes=S(q);q+=2;}
                int format;if((controls&4096)!=0){if((controls&8192)==0)throw new InvalidDataException("Invalid event compression.");format=previousFormat;}else{format=S(q);q+=2;}
                if((format&1920)!=0){q+=(format&128)!=0?6:4;previousSize=(controls&8448)!=0?41:20;previousFormat=format;}
                else if((format&63488)!=0){q+=(format&63488) switch{16384 or 32768 or 8192=>4,4096 or 2048=>6,_=>0};previousSize=(controls&8448)!=0?33:16;previousFormat=format;}
                else if((format&23)!=0)
                {
                    previousSize=(controls&8448)!=0?(bytes*2|1):bytes;previousFormat=format;
                    int target=previousSize;if((controls&8192)!=0)target-=2;if((controls&4096)!=0)target-=2;
                    q+=(format&3) switch{3=>36,2=>24,1=>12,_=>(format&20) switch{4=>68,16=>16,_=>0}};
                    if((format&8)!=0 && q-start<target){int n=I(q);q=checked(q+4+(2*n-1)*4);}
                }
            }
        }
        if(q!=end)throw new InvalidDataException($"Event layout mismatch at {p:X8}: {q:X8}/{end:X8}.");
    }

    private void Gao(Record r)
    {
        int p=r.Offset,end=p+r.Size;
        if(Text(p,4)!=".gao")throw new InvalidDataException($"Expected GAO {r.Key:X8} at {p:X8}.");
        uint flags=U(p+8);r.Flags=flags;int q=p+90+((flags&0x80000)!=0?48:24);
        if((flags&0x1000)!=0)
        {
            if((flags&0x4000)!=0)
            {
                r.Geometry=U(q);r.Material=U(q+4);Add(r.Geometry,'m');Add(r.Material,'m');q+=16;
                int rli=I(q);q=checked(q+4+rli*4);
            }
            if((flags&0x400000)!=0){r.Parent=U(q);Add(r.Parent,'l');Add(r.Parent,'l');q+=72;}
            if((flags&2)!=0){r.Events=U(q);r.Shape=U(q+4);r.Actions=U(q+8);r.Skeleton=U(q+12);Add(r.Skeleton,'b');q+=16;}
            if((flags&0x200000)!=0){int n=I(q);q+=4;if((flags&0x1000000)!=0)for(int i=0;i<n;i++,q+=8)Add(U(q),'l');else q=checked(q+n*68);}
        }
        if((flags&0x2000)!=0)
        {
            r.Prefab=U(q);q+=4;if((flags&0x800000)!=0)Add(r.Prefab,'k');
            uint modifiers=U(q);q+=4;q+=14;
            if((flags&0x20)!=0){Add(U(q),'q');q+=4;}
            if((flags&0x2000000)!=0){Add(U(q),'i');q+=4;}
            if((flags&0x8000000)!=0){Add(U(q),'u');q+=4;}
            if((flags&0x40000)!=0){Add(U(q),'w');q+=4;}
            if((flags&0x10)!=0){Add(U(q),'m');q+=4;}
            if((flags&0x40)!=0)q+=92;
            if(modifiers!=0)Modifiers(ref q,end);
        }
        if((flags&0x600)!=0){Add(U(q),'d');q+=4;}
        if((flags&0x100)!=0){Add(U(q),'f');q+=4;}
        int nlen=I(q);q+=4;r.Name=Text(q,nlen);q+=nlen;
        if(q!=end)throw new InvalidDataException($"GAO layout mismatch {r.Key:X8} at {p:X8}: {q:X8}/{end:X8}.");
    }
    private void Modifiers(ref int q,int end)
    {
        for(int n=0;n<1024;n++)
        {
            uint type=U(q);q+=4;if(type==uint.MaxValue)return;q+=4;int start=q;
            if(type==3 && (U(q+24)&0x10)==0){Add(U(q+4),'l');Add(U(q+8),'l');Add(U(q+20),'l');}
            if(type==7){int version=I(q);uint extra=version==28?U(q+12):uint.MaxValue;if(extra==uint.MaxValue)Add(U(q+(version==28?16:12)),'l');}
            if(type==8 && (U(q+4)&0x800)!=0 && U(q)>272)Add(U(q+12),'u');
            if(type==9)Add(U(q+12+I(q+8)*8),'m');
            int used=PcTextureLoader.ModifierSize(b,q,end,type);current?.Modifiers.Add((type,start,used));q=checked(start+used);if(q>end)throw new InvalidDataException("Modifier outside GAO.");
        }
        throw new InvalidDataException("Modifier dependency budget exceeded.");
    }
    private Result Run()
    {
        try
        {
            int size=I(0);if(size<8 || size%8!=0 || size>b.Length-4)throw new InvalidDataException("Not a PC WOL stream.");
            var roots=new List<uint>();for(int p=4;p<4+size;p+=8){if(Text(p+4,4)!=".wow" && U(p)!=0)throw new InvalidDataException("Not a PC WOL list.");roots.Add(U(p));}
            // Original loader bypasses SPE for the built-in FIX container (FF0084DF).
            fixedResources = roots.Count == 1 && roots[0] == 0x5E0084DE;
            cursor=4+size;
            var mergedWorldKeys = new HashSet<uint>();
            foreach(uint root in roots)
            {
                Add(root,'z');Drain();
                if(!result.Resources.TryGetValue(root,out var world))continue;
                if(!result.Resources.TryGetValue(world.Group,out var list))continue;
                var worldMembers = list.References.Select(item => cache.GetValueOrDefault(item.Key))
                    .Where(item => item?.Kind == 'l').Cast<Record>().ToList();
                var memberKeys = worldMembers.Select(item => item.Key).ToHashSet();
                var processing = new HashSet<uint>();
                List<Record> GroupMembers(uint groupKey)
                {
                    if (!cache.TryGetValue(groupKey, out var group)
                        || group.References.FirstOrDefault() is not { } listRef
                        || !cache.TryGetValue(listRef.Key, out var groupList)) return [];
                    return groupList.References.Select(item => cache.GetValueOrDefault(item.Key))
                        .Where(item => item?.Kind == 'l').Cast<Record>().ToList();
                }
                void Expand(Record gao)
                {
                    if ((gao.Flags & 2) == 0 || !processing.Add(gao.Key)) return;
                    if (processing.Count > 256) throw new InvalidDataException("FF0 action-group nesting exceeds limit.");
                    Add(gao.Shape,'c');Add(gao.Actions,'k');Drain();
                    foreach (var child in GroupMembers(gao.Actions))
                    {
                        if (memberKeys.Contains(child.Key)) continue;
                        // y.a(z,e,l): action-list objects become members of this world.
                        Expand(child);
                        if (memberKeys.Add(child.Key)) worldMembers.Add(child);
                        if ((child.Flags & 0x800000) != 0)
                            foreach (var nested in GroupMembers(child.Prefab))
                            {
                                Expand(nested);
                                if (memberKeys.Add(nested.Key)) worldMembers.Add(nested);
                            }
                    }
                    if(gao.Skeleton is 0 or uint.MaxValue){Add(gao.Events,'i');Drain();}
                    processing.Remove(gao.Key);
                }
                foreach (var item in worldMembers.ToArray())
                    if ((item.Flags & 3) != 1) Expand(item);
                world.WorldMembers.AddRange(worldMembers);
                if(worldMembers.Any(member=>(member.Flags&2)!=0))world.ActorResources=new(cache);
                mergedWorldKeys.UnionWith(memberKeys);
                // Iris y.b(z): evict external parents AND prefab members, and detach
                // every reference to each evicted parent. This runs on the merged world.
                void EvictExternal(uint key)
                {
                    if (mergedWorldKeys.Contains(key) || !cache.Remove(key)) return;
                    foreach (uint member in mergedWorldKeys)
                        if (cache.TryGetValue(member, out var child) && child.Parent == key)
                            child.Parent = uint.MaxValue;
                }
                foreach (uint objectKey in mergedWorldKeys)
                {
                    if (!cache.TryGetValue(objectKey, out var gao)) continue;
                    if ((gao.Flags & 0x400000) != 0 && gao.Parent != uint.MaxValue)
                        EvictExternal(gao.Parent);
                    if ((gao.Flags & 0x800000) != 0 && gao.Prefab is not (0 or uint.MaxValue)
                        && cache.TryGetValue(gao.Prefab, out var prefab)
                        && prefab.References.FirstOrDefault() is { } groupRef
                        && cache.TryGetValue(groupRef.Key, out var members))
                        foreach (var member in members.References) EvictExternal(member.Key);
                }
                if (root != roots.First(key => key is not (0 or uint.MaxValue))) cache.Remove(root);
            }
            if(cursor!=b.Length)throw new InvalidDataException($"FF0 traversal left {b.Length-cursor} bytes at {cursor:X8}.");
        }
        catch(Exception ex) when(ex is InvalidDataException or OverflowException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {result.Error=$"{ex.Message} Last: {current?.Kind} {current?.Key:X8}, cursor {cursor:X8}.";}
        result.End=cursor;return result;
    }
    internal static Result Read(byte[] bytes,string archivePath)
    {
        string path=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath))!,"jade.spe");
        if(!File.Exists(path))return new Result{Error="jade.spe not found next to archive."};
        byte[] raw=File.ReadAllBytes(path);if(raw.Length%4!=0)return new Result{Error="Invalid jade.spe size."};
        var special=new HashSet<uint>();for(int p=0;p<raw.Length;p+=4)special.Add(Preview.U32(raw,p));
        return new PcSceneLoader(bytes,special).Run();
    }
    internal static void Audit(string archivePath,string output)
    {
        var archive=new Archive(archivePath);
        var rows=archive.Entries.Where(e=>(e.Key&0xfff00000)==0xff000000).Select(entry=>
        {
            try { var r=Read(archive.Read(entry),archivePath);return new {entry.Id,r.Complete,r.Error,r.End,Records=r.Records.Count}; }
            catch(Exception e) when(e is InvalidDataException or IOException){return new {entry.Id,Complete=false,Error=(string?)e.Message,End=0,Records=0};}
        }).ToArray();
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Verify(string archivePath,string sceneId,string output)
    {
        var archive=new Archive(archivePath);var entry=archive.Entries.First(e=>e.Id.Equals(sceneId,StringComparison.OrdinalIgnoreCase));
        var replay=Read(archive.Read(entry),archivePath);
        File.WriteAllText(output,JsonSerializer.Serialize(new {replay.Complete,replay.Error,replay.End,replay.SpeSkipped,replay.CachedSkipped,
            Worlds=replay.Worlds.Select(w=>new{Key=w.Key.ToString("X8"),w.Name,Members=w.WorldMembers.Select(r=>new{Key=r.Key.ToString("X8"),r.Offset,r.Name})}),
            Records=replay.Records.Select(r=>new{Key=r.Key.ToString("X8"),Kind=r.Kind.ToString(),r.Offset,r.Size,r.Name,Geometry=r.Geometry.ToString("X8"),Material=r.Material.ToString("X8"),References=r.References.Select(x=>new{Key=x.Key.ToString("X8"),Kind=x.Kind.ToString()})})},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FF0 {sceneId}: {replay.Complete}, {replay.Records.Count} records, {replay.End}; {replay.Error}");
    }
}
