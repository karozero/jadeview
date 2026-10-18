using System.Numerics;

namespace JadeView;

public static class AnimationReader
{
    public static AnimationClip Read(byte[] b,int start,int size)
    {
        int q=start,end=start+size,previousSize=0,previousFormat=0;
        void Need(int n){if(n<0 || q>end-n)throw new InvalidDataException("Truncated animation record.");}
        int U8(){Need(1);return b[q++];}
        int U16(){Need(2);int v=Preview.U16(b,q);q+=2;return v;}
        int I16()=>(short)U16();
        uint U32(){Need(4);uint v=Preview.U32(b,q);q+=4;return v;}
        float F()=>BitConverter.UInt32BitsToSingle(U32());
        Vector3 V()=>new(F(),F(),F());
        var clip=new AnimationClip{Loaded=true};int channels=U16();U16();
        for(int channel=0;channel<channels;channel++)
        {
            int flags=U16(),bone=I16();uint target=U32();int mode=flags&0x3f00;
            if((flags&0x8000)!=0){if(mode==0)U32();target=0;}else target=U32();
            int count=checked((int)U32());if(count>100000)throw new InvalidDataException("Animation key budget exceeded.");
            var track=new AnimationTrack{Bone=bone,Target=target};clip.Tracks.Add(track);int firstFlags=0;float time=0;
            for(int frame=0;frame<count;frame++)
            {
                int duration=(mode&512)!=0?U8():U16();
                int f;if((mode&2048)!=0 && frame>0)f=firstFlags&~4;else{f=U16();if(frame==0)firstFlags=f;}
                f&=~2;var key=new AnimationKey{Time=time,Duration=duration/60f,Flags=f};track.Keys.Add(key);time+=key.Duration;
                if((f&1984)==64){int n=checked((int)U32());Need(n*8);q+=n*8;continue;}
                if((f&1984)!=128)continue;
                if((f&32)!=0){U32();clip.Unsupported++;continue;}
                int controls=frame>0?mode:((mode&8192)!=0?256:0),p=q;
                int bytes=(controls&8192)!=0?previousSize/2:U16();
                int format=(controls&4096)!=0?previousFormat:U16();key.Format=format;
                if((format&1920)!=0)
                {
                    if((format&128)!=0)
                    {
                        float x=I16()/32767f,y=I16()/32767f,z=I16()/32767f;
                        key.Rotation=Quaternion.Normalize(new(x,y,z,MathF.Sqrt(Math.Max(0,1-x*x-y*y-z*z))));
                    }
                    else {Need(4);q+=4;clip.Unsupported++;}
                    previousSize=(controls&8448)!=0?41:20;previousFormat=format;
                }
                else if((format&63488)!=0)
                {
                    int type=format&63488;float x=0,y=0,z=0;
                    if(type is 2048 or 4096){x=I16()/1024f;y=I16()/1024f;z=I16()/1024f;}
                    else{float s=I16()/1024f,a=(sbyte)U8()/1024f,c=(sbyte)U8()/1024f;if(type==8192){x=s;y=a;z=c;}else if(type==16384){x=a;y=s;z=c;}else if(type==32768){x=a;y=c;z=s;}else clip.Unsupported++;}
                    key.Position=new(x,y,z);previousSize=(controls&8448)!=0?33:16;previousFormat=format;
                }
                else if((format&23)!=0)
                {
                    previousSize=(controls&8448)!=0?(bytes*2|1):bytes;previousFormat=format;
                    int targetSize=previousSize;if((controls&8192)!=0)targetSize-=2;if((controls&4096)!=0)targetSize-=2;
                    switch(format&3)
                    {
                        case 3:key.Control2=V();key.Control1=V();key.Position=V();break;
                        case 2:key.Control1=V();key.Position=V();break;
                        case 1:key.Position=V();break;
                        default:
                            if((format&20)==4){Need(68);var m=AnimationMath.Matrix(b,q);q+=68;Matrix4x4.Decompose(m,out var s,out var r,out var t);key.Rotation=r;}
                            else if((format&20)==16)key.Rotation=Quaternion.Normalize(new(F(),F(),F(),F()));
                            break;
                    }
                    if((format&8)!=0 && q-p<targetSize){int n=checked((int)U32());Need((2*n-1)*4);q+=(2*n-1)*4;clip.Unsupported++;}
                }
            }
            clip.Duration=Math.Max(clip.Duration,time);
        }
        if(q!=end)throw new InvalidDataException($"Animation cursor mismatch {q:X8}/{end:X8}.");
        return clip;
    }
}
