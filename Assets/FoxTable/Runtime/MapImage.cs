using System;

namespace FoxTable
{
    public static class MapImage
    {
        // Inspect dimensions before allocating an Android texture for a compressed image.
        public static bool Fits(byte[] data)
        {
            if(data==null || data.Length<24 || data.Length>6*1024*1024)return false;
            long width=0,height=0;
            if(data[0]==137 && data[1]==80 && data[2]==78 && data[3]==71 && data[12]==73 && data[13]==72 && data[14]==68 && data[15]==82)
            {width=Big(data,16);height=Big(data,20);}
            else if(data[0]==255 && data[1]==216)
            {
                int p=2;
                while(p+3<data.Length)
                {
                    if(data[p++]!=255)return false;
                    while(p<data.Length && data[p]==255)p++;
                    if(p>=data.Length)return false;
                    int marker=data[p++];
                    if(marker==0xd9 || marker==0xda)break;
                    if(marker==0xd8 || marker==1 || marker>=0xd0 && marker<=0xd7)continue;
                    if(p+1>=data.Length)return false;
                    int length=(data[p]<<8)|data[p+1];
                    if(length<2 || p+length>data.Length)return false;
                    if(marker>=0xc0 && marker<=0xcf && marker!=0xc4 && marker!=0xc8 && marker!=0xcc)
                    {
                        if(length<7)return false;
                        height=(data[p+3]<<8)|data[p+4];width=(data[p+5]<<8)|data[p+6];break;
                    }
                    p+=length;
                }
            }
            return width>0 && height>0 && width<=8192 && height<=8192 && width*height<=16*1024*1024;
        }
        private static long Big(byte[] data,int p)=>(long)data[p]<<24 | (long)data[p+1]<<16 | (long)data[p+2]<<8 | data[p+3];
    }
}
