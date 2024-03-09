﻿using System.Collections.Generic;

namespace CameraLib
{
    public class FrameFormat : FrameFormatDto
    {
        public FrameFormat(int width = 0, int height = 0, string format = "", double fps = 0)
        {
            Width = width;
            Height = height;
            Format = format;
            Fps = fps;
        }

        public static readonly Dictionary<int, string> Codecs = new Dictionary<int, string>
        {
            {-1, "UNKN"}, // Unknown
            {810961203, "3IV0"}, // 'result := 'MPEG4-based codec 3ivx'
            {827738419, "3IV1"}, //'MPEG4-based codec 3ivx'
            {844515635, "3IV2"}, //'MPEG4-based codec 3ivx'
            {1482049843, "3IVX"}, //'MPEG4-based codec 3ivx'
            {1129529665, "AASC"}, //'Autodesk Animator codec'
            {1381581377, "ABYR"}, //'Kensington codec'"},
            {1229800769, "AEMI"}, //'Array VideoONE MPEG1-I Capture'
            {1129072193, "AFLC"}, //'Autodesk Animator FLC (256 color)'
            {1229735489, "AFLI"}, //'Autodesk Animator FLI (256 color)'
            {1196444993, "AMPG"}, //'Array VideoONE MPEG'
            {1296649793, "ANIM"}, //'Intel RDX'
            {825512001, "AP41"}, //'AngelPotion Definitive (hack MS MP43)'
            {827740993, "ASV1"}, //'Asus Video V1'"},
            {844518209, "ASV2"}, //'Asus Video V2'"},
            {1482052417, "ASVX"}, //'Asus Video 2.0'"},
            {844256577, "AUR2"}, //'Aura 2 Codec - YUV 422'"},
            {1095914817, "AURA"}, //'Aura 1 Codec - YUV 411'"},
            {1245992513, "AVDJ"}, //'Avid Motion JPEG'"},
            {1314018881, "AVRN"}, //'Avid Motion JPEG'"},
            {1380997697, "AZPR"}, //'Quicktime Apple Video'"},
            {1263421762, "BINK"}, //'Bink Video (RAD Game Tools) (256 color)'"},
            {808604738, "BT20"}, //'Conexant (ex Brooktree) ProSummer MediaStream'"},
            {1447253058, "BTCV"}, //'Conexant Composite Video'"},
            {1129731138, "BTVC"}, //'Conexant Composite Video'"},
            {808539970, "BW10"}, //'Data Translation Broadway MPEG Capture/Compression'"},
            {842089283, "CC12"}, //'Intel YUV12 Codec'"},
            {1129727043, "CDVC"}, //'Canopus DV Codec'"},
            {1128482371, "CFCC"}, //'Conkrete DPS Perception Motion JPEG'"},
            {1229211459, "CGDI"}, //'Camcorder Video (MS Office 97)'"},
            {1296123971, "CHAM"}, //'WinNow Caviara Champagne'"},
            {1196444227, "CJPG"}, //'Creative Video Blaster Webcam Go JPEG';"},
            {1380600899, "CLJR"}, //'Cirrus Logic YUV 4:1:1'"},
            {1264143683, "CMYK"}, //'Common Data Format in Printing'"},
            {1095520323, "CPLA"}, //'Weitek YUV 4:2:0 Planar'"},
            {1296126531, "CRAM"}, //'Microsoft Video 1'"},
            {1145656899, "CVID"}, //'Cinepak by Radius YUV 4:2:2'"},
            {1414289219, "CWLT"}, //'Microsoft Color WLT DIB'"},
            {1448433987, "CYUV"}, //'Creative Labs YUV 4:2:2'"},
            {1498765635, "CYUY"}, //'ATI Technologies YUV'"},
            {825635396, "D261"}, //'H.261'"},
            {859189828, "D263"}, //'H.263'"},
            {4344132, "DIB "}, //'Full Frames (Uncompressed)'"},
            {827738436, "DIV1"}, //'FFmpeg-4 V1 (hacked MS MPEG-4 V1)'"},
            {844515652, "DIV2"}, //'FFmpeg-4 V2 (hacked MS MPEG-4 V2)'"},
            {861292868, "DIV3"}, //'DivX;-) MPEG-4 Low-Motion'"},
            {878070084, "DIV4"}, //'DivX;-) MPEG-4 Fast-Motion'"},
            {894847300, "DIV5"}, //'DivX MPEG-4'"},
            {911624516, "DIV6"}, //'DivX MPEG-4'"},
            {1482049860, "DIVX"}, //'OpenDivX (DivX 4.0 and later)'"},
            {826428740, "DMB1"}, //'Matrox Rainbow Runner hardware compression (Motion JPEG)'"},
            {843205956, "DMB2"}, //'Motion JPEG codec used by Paradigm'"},
            {810766404, "DPS0"}, //'DPS Reality Motion JPEG'"},
            {1129533508, "DPSC"}, //'DPS PAR Motion JPEG'"},
            {1146508100, "DSVD"}, //'Microsoft DirectShow DV'"},
            {1262703940, "DUCK"}, //'True Motion 1.0'"},
            {892491332, "DV25"}, //'Matrox DVCPRO codec'"},
            {808801860, "DV50"}, //'Matrox DVCPRO50 codec'"},
            {4412996, "DVC "}, //'MainConcept DV Codec'"},
            {1346590276, "DVCP"}, //'Sony Digital Video (DV)'"},
            {843404868, "DVE2"}, //'DVE-2 Videoconferencing Codec'"},
            {1145591364, "DVHD"}, //'DV 1125 lines at 30.00 Hz or 1250 lines at 25.00 Hz'"},
            {1095587396, "DVMA"}, //'Darim Vision DVMPEG'"},
            {827545156, "DVS1"}, //'DV compressed in SD (SDL)'"},
            {1146312260, "DVSD"}, //'Sony Digital Video (DV) 525 lines at 29.97 Hz or 625 lines at 25.00 Hz'"},
            {827872836, "DVX1"}, //'DVX1000SP Video Decoder'"},
            {844650052, "DVX2"}, //'DVX2000S Video Decoder'"},
            {861427268, "DVX3"}, //'DVX3000S Video Decoder'"},
            {808802372, "DX50"}, //'DivX 5.0 codec'"},
            {827611204, "DXT1"}, //'DirectX Compressed Texture'"},
            {844388420, "DXT2"}, //'DirectX Compressed Texture'"},
            {861165636, "DXT3"}, //'DirectX Compressed Texture'"},
            {877942852, "DXT4"}, //'DirectX Compressed Texture'"},
            {894720068, "DXT5"}, //'DirectX Compressed Texture'"},
            {1129601092, "DXTC"}, //'DirectX Texture Compression'"},
            {810634053, "EKQ0"}, //'Else graphics card codec'"},
            {810241093, "ELK0"}, //'Else graphics card codec'"},
            {1446137157, "EM2V"}, //'Etymonix MPEG-2 I-frame'"},
            {1346589509, "ESCP"}, //'Eidos Technologies Escape codec'"},
            {827741253, "ETV1"}, //'eTreppid Video Codec'"},
            {844518469, "ETV2"}, //'eTreppid Video Codec'"},
            {1129731141, "ETVC"}, //'eTreppid Video Codec'"},
            {1347046470, "FLJP"}, //'Field Encoded Motion JPEG (Targa emulation)'"},
            {1096241734, "FRWA"}, //'Darim Vision Forward Motion JPEG with Alpha-channel'"},
            {1146573382, "FRWD"}, //'Darim Vision Forward Motion JPEG'"},
            {1415008838, "FRWT"}, //'Darim Vision Forward Motion JPEG'"},
            {1431786054, "FRWU"}, //'Darim Vision Forward Uncompressed'"},
            {826693190, "FVF1"}, //'Fractal Video Frame'"},
            {1464227398, "FVFW"}, //'ff MPEG-4 based on XviD codec'"},
            {1465535559, "GLZW"}, //'Motion LZW by gabest@freemail.hu'"},
            {1195724871, "GPEG"}, //'Motion JPEG by gabest@freemail.hu (with floating point)'"},
            {1414289223, "GWLT"}, //'Microsoft Greyscale WLT DIB'"},
            {808858184, "H260"}, //'H.260'"},
            {825635400, "H261"}, //'H.261'"},
            {842412616, "H262"}, //'H.262'"},
            {859189832, "H263"}, //'Intel ITU H.263'"},
            {875967048, "H264"}, //'H.264'"},
            {892744264, "H265"}, //'H.265'"},
            {909521480, "H266"}, //'H.266'"},
            {926298696, "H267"}, //'H.267'"},
            {943075912, "H268"}, //'H.268'"},
            {959853128, "H269"}, //'H.263 for POTS-based videoconferencing'"},
            {1431914056, "HFYU"}, //'Huffman Lossless Codec YUV and RGB formats (with Alpha-channel)'"},
            {1380142408, "HMCR"}, //'Rendition Motion Compensation Format'"},
            {1381125448, "HMRR"}, //'Rendition Motion Compensation Format'"},
            {859189833, "I263"}, //'Intel ITU H.263'"},
            {808596553, "I420"}, //'Intel Indeo 4 H.263'"},
            {5128521, "IAN "}, //'Indeo 4 (RDX) Codec'"},
            {1398161737, "IAVS"}, //'interleaved audio and video stream'"},
            {1112294217, "ICLB"}, //'CellB Videoconferencing Codec'"},
            {1380927305, "IGOR"}, //'Power DVD'"},
            {1196444233, "IJPG"}, //'Intergraph JPEG'"},
            {1129729097, "ILVC"}, //'Intel Layered Video'"},
            {1381387337, "ILVR"}, //'ITU H.263+ Codec'"},
            {1447317577, "IPDV"}, //'Giga AVI DV Codec'"},
            {825381449, "IR21"}, //'Intel Indeo 2.1'"},
            {1463898697, "IRAW"}, //'Intel YUV Uncompressed'"},
            {1448695113, "IUYV"}, //'Interlaced version of UYVY (line order 0, 2, 4,....,1, 3, 5....)'"},
            {808670793, "IV30"}, //'Intel Indeo Video 3'"},
            {825448009, "IV31"}, //'Intel Indeo Video 3.1'"},
            {842225225, "IV32"}, //'Intel Indeo Video 3.2'"},
            {859002441, "IV33"}, //'Intel Indeo Video 3.3'"},
            {875779657, "IV34"}, //'Intel Indeo Video 3.4'"},
            {892556873, "IV35"}, //'Intel Indeo Video 3.5'"},
            {909334089, "IV36"}, //'Intel Indeo Video 3.6'"},
            {926111305, "IV37"}, //'Intel Indeo Video 3.7'"},
            {942888521, "IV38"}, //'Intel Indeo Video 3.8'"},
            {959665737, "IV39"}, //'Intel Indeo Video 3.9'"},
            {808736329, "IV40"}, //'Intel Indeo Video 4.0'"},
            {825513545, "IV41"}, //'Intel Indeo Video 4.1'"},
            {842290761, "IV42"}, //'Intel Indeo Video 4.2'"},
            {859067977, "IV43"}, //'Intel Indeo Video 4.3'"},
            {875845193, "IV44"}, //'Intel Indeo Video 4.4'"},
            {892622409, "IV45"}, //'Intel Indeo Video 4.5'"},
            {909399625, "IV46"}, //'Intel Indeo Video 4.6'"},
            {926176841, "IV47"}, //'Intel Indeo Video 4.7'"},
            {942954057, "IV48"}, //'Intel Indeo Video 4.8'"},
            {959731273, "IV49"}, //'Intel Indeo Video 4.9'"},
            {808801865, "IV50"}, //'Intel Indeo Video 5.0 Wavelet'"},
            {825514313, "IY41"}, //'Interlaced version of Y41P (line order 0, 2, 4,....,1, 3, 5....)'"},
            {827677001, "IYU1"}, //'12 bit format used in mode 2 of the IEEE 1394 Digital Camera 1.04 spec'"},
            {844454217, "IYU2"}, //'24 bit format used in mode 2 of the IEEE 1394 Digital Camera 1.04 spec'"},
            {1448433993, "IYUV"}, //'Intel Indeo iYUV 4:2:0'"},
            {1195724874, "JPEG"}, //'Still Image JPEG DIB'"},
            {1279742026, "JPGL"}, //'DIVIO JPEG Light for WebCams'"},
            {1129729355, "KMVC"}, //'Karl Morton Video Codec'"},
            {1145128268, "LEAD"}, //'LEAD Video Codec'"},
            {1196444236, "LJPG"}, //'LEAD Motion JPEG Codec'"},
            {1297503052, "LSVM"}, //'Vianet Lighting Strike Vmail (Streaming)'"},
            {825635405, "M261"}, //'Microsoft H.261'"},
            {859189837, "M263"}, //'Microsoft H.263'"},
            {844313677, "M4S2"}, //'Microsoft MPEG-4 (hacked MS MPEG-4)'"},
            {842089293, "MC12"}, //'ATI Motion Compensation Format'"},
            {1296122701, "MCAM"}, //'ATI Motion Compensation Format'"},
            {1146504269, "MDVD"}, //'Alex MicroDVD Video (hacked MS MPEG-4)'"},
            {1127369293, "MJ2C"}, //'Morgan Multimedia JPEG2000 Compression'"},
            {1095780941, "MJPA"}, //'Pinnacle Motion JPEG with Alpha-channel'"},
            {1112558157, "MJPB"}, //'Motion JPEG codec'"},
            {1196444237, "MJPG"}, //'IBM Motion JPEG including Huffman Tables'"},
            {1397050701, "MMES"}, //'Matrox MPEG-2 I-frame'"},
            {842289229, "MP42"}, //'Microsoft MPEG-4 V2'"},
            {859066445, "MP43"}, //'Microsoft MPEG-4 V3'"},
            {1395937357, "MP4S"}, //'Microsoft MPEG-4 (hacked MS MPEG-4)'"},
            {1446269005, "MP4V"}, //'Microsoft MPEG-4 (hacked MS MPEG-4)'"},
            {1195724877, "MPEG"}, //'Chromatic MPEG 1 Video I Frame'"},
            {826757197, "MPG1"}, //'FFmpeg-1'"},
            {843534413, "MPG2"}, //'FFmpeg-1'"},
            {860311629, "MPG3"}, //'Same as Low motion DivX MPEG-4'"},
            {877088845, "MPG4"}, //'Microsoft MPEG-4 V1'"},
            {1229410381, "MPGI"}, //'Sigma Design MPEG-1 I-frame'"},
            {1196314701, "MPNG"}, //'Motion PNG codec'"},
            {1094931021, "MRCA"}, //'FAST Multimedia MR Codec'"},
            {1162629709, "MRLE"}, //'Microsoft Run Length Encoding'"},
            {827544397, "MSS1"}, //'Windows Screen Video'"},
            {1129730893, "MSVC"}, //'Microsoft Video 1'"},
            {1213879117, "MSZH"}, //'Lossless codec (ZIP compression)'"},
            {827872333, "MTX1"}, //'Matrox codec'"},
            {844649549, "MTX2"}, //'Matrox codec'"},
            {861426765, "MTX3"}, //'Matrox codec'"},
            {878203981, "MTX4"}, //'Matrox codec'"},
            {894981197, "MTX5"}, //'Matrox codec'"},
            {911758413, "MTX6"}, //'Matrox codec'"},
            {928535629, "MTX7"}, //'Matrox codec'"},
            {945312845, "MTX8"}, //'Matrox codec'"},
            {962090061, "MTX9"}, //'Matrox codec'"},
            {827742029, "MWV1"}, //'Aware Motion Wavelets'"},
            {1230389582, "NAVI"}, //'nAVI video codec (hacked MS MPEG-4)'"},
            {808473678, "NT00"}, //'NewTek LigtWave HDTV YUV with Alpha-channel'"},
            {827216974, "NTN1"}, //'Nogatech Video Compression 1'"},
            {827741518, "NUV1"}, //'NuppelVideo'"},
            {810767950, "NVS0"}, //'Nvidia texture compression format'"},
            {827545166, "NVS1"}, //'Nvidia texture compression format'"},
            {844322382, "NVS2"}, //'Nvidia texture compression format'"},
            {861099598, "NVS3"}, //'Nvidia texture compression format'"},
            {877876814, "NVS4"}, //'Nvidia texture compression format'"},
            {894654030, "NVS5"}, //'Nvidia texture compression format'"},
            {810833486, "NVT0"}, //'Nvidia texture compression format'"},
            {827610702, "NVT1"}, //'Nvidia texture compression format'"},
            {844387918, "NVT2"}, //'Nvidia texture compression format'"},
            {861165134, "NVT3"}, //'Nvidia texture compression format'"},
            {877942350, "NVT4"}, //'Nvidia texture compression format'"},
            {894719566, "NVT5"}, //'Nvidia texture compression format'"},
            {1129727056, "PDVC"}, //'Panasonic DV codec'"},
            {1448494928, "PGVV"}, //'Radius Video Vision Telecast (adaptive JPEG)'"},
            {1330464848, "PHMO"}, //'Photomotion'"},
            {827148624, "PIM1"}, //'Pegasus Imaging codec'"},
            {843925840, "PIM2"}, //'Pegasus Imaging codec'"},
            {1246579024, "PIMJ"}, //'Pegasus Imaging PICvideo Lossless JPEG'"},
            {1514493520, "PVEZ"}, //'Horizons Technology PowerEZ codec'"},
            {1296914000, "PVMM"}, //'PacketVideo Corporation MPEG-4'"},
            {844584528, "PVW2"}, //'Pegasus Imaging Wavelet 2000'"},
            {808333649, "Q1.0"}, //'Q-Team QPEG 1.0'"},
            {1195724881, "QPEG"}, //'Q-Team QPEG 1.1'"},
            {1363497041, "QPEQ"}, //'Q-Team QPEG 1.1'"},
            {5718354, "RAW "}, //'Full Frames (Uncompressed)'"},
            {4343634, "RGB "}, //'Full Frames (Uncompressed)'"},
            {1094862674, "RGBA"}, //'Raw RGB with alpha'"},
            {1413629778, "RGBT"}, //'Uncompressed RGB with transparency'"},
            {876956754, "RLE4"}, //'Run length encoded 4bpp RGB image'"},
            {944065618, "RLE8"}, //'Run length encoded 8bpp RGB image'"},
            {4541522, "RLE "}, //'Raw RGB with arbitrary sample packing within a pixel'"},
            {877677906, "RMP4"}, //'REALmagic MPEG-4 Video Codec'"},
            {1096437842, "RPZA"}, //'Apple Video 16 bit 'road pizza''"},
            {825381970, "RT21"}, //'Intel Real Time Video 2.1'"},
            {809784658, "RUD0"}, //'Rududu video codec'"},
            {808539730, "RV10"}, //'RealVideo codec'"},
            {858871378, "RV13"}, //'RealVideo codec'"},
            {808605266, "RV20"}, //'RealVideo G2'"},
            {808670802, "RV30"}, //'RealVideo 8'"},
            {5789266, "RVX "}, //'Intel RDX'"},
            {842150995, "S422"}, //'VideoCap C210 YUV Codec'"},
            {1128481875, "SDCC"}, //'Sun Digital Camera Codec'"},
            {1129137747, "SFMC"}, //'Crystal Net SFM Codec'"},
            {1196444243, "SJPG"}, //'White Pine (ex Paradigm Matrix) Motion JPEG'"},
            {4410707, "SMC "}, //'Apple Graphics (SMC) codec (256 color)'"},
            {1129532755, "SMSC"}, //'Radius proprietary codec'"},
            {1146309971, "SMSD"}, //'Radius proprietary codec'"},
            {1448299859, "SMSV"}, //'WorldConnect Wavelet Streaming Video'"},
            {1195987027, "SPIG"}, //'Radius Spigot'"},
            {1129074771, "SPLC"}, //'Splash Studios ACM Audio Codec'"},
            {844779859, "SQZ2"}, //'Microsoft VXTreme Video Codec V2'"},
            {1096176723, "STVA"}, //'ST CMOS Imager Data (Bayer)'"},
            {1112953939, "STVB"}, //'ST CMOS Imager Data (Nudged Bayer)'"},
            {1129731155, "STVC"}, //'ST CMOS Imager Data (Bunched)'"},
            {1482052691, "STVX"}, //'ST CMOS Imager Data (Extended)'"},
            {1498829907, "STVY"}, //'ST CMOS Imager Data (Extended with Correction Data)'"},
            {808539731, "SV10"}, //'Sorenson Media Video R1'"},
            {827414099, "SVQ1"}, //'Sorenson Video (Apple Quicktime 3)'"},
            {860968531, "SVQ3"}, //'Sorenson Video 3 (Apple Quicktime 5)'"},
            {808596564, "T420"}, //'Toshiba YUV 4:2:0'"},
            {1397574740, "TLMS"}, //'TeraLogic Motion Infraframe Codec A'"},
            {1414745172, "TLST"}, //'TeraLogic Motion Infraframe Codec B'"},
            {808602964, "TM20"}, //'Duck TrueMotion 2.0'"},
            {1093815636, "TM2A"}, //'Duck TrueMotion Archiver 2.0'"},
            {1479691604, "TM2X"}, //'Duck TrueMotion 2X'"},
            {1128877396, "TMIC"}, //'TeraLogic Motion Intraframe Codec 2'"},
            {1414483284, "TMOT"}, //'TrueMotion Video Compression'"},
            {808604244, "TR20"}, //'Duck TrueMotion RT 2.0'"},
            {1128485716, "TSCC"}, //'TechSmith Screen Capture Codec'"},
            {808539732, "TV10"}, //'Tecomac Low-Bit Rate Codec'"},
            {1246582356, "TVMJ"}, //'Field Encoded Motion JPEG (Targa emulation)'"},
            {1311791444, "TY0N"}, //'Trident Decompression Driver'"},
            {1127373140, "TY2C"}, //'Trident Decompression Driver'"},
            {1311922516, "TY2N"}, //'Trident Decompression Driver'"},
            {859189845, "U263"}, //'UB Video StreamForce H.263'"},
            {1146045269, "UCOD"}, //'ClearVideo (fractal compression-based codec)'"},
            {1230261333, "ULTI"}, //'IBM Corp. Ultimotion'"},
            {1447975253, "UYNV"}, //'A direct copy of UYVY registered by NVidia to work around problems in some old codecs which
                                  //did not like hardware which offered more than 2 UYVY surfaces'"},
            {1347836245, "UYVP"}, //'YCbCr 4:2:2 extended precision 10-bits per component in U0Y0V0Y1 order'"},
            {1498831189, "UYVY"}, //'YUV 4:2:2 (Y sample at every pixel, U and V sampled at every second pixel
                                  //horizontally on each line)'"},
            {825635414, "V261"}, //'Lucent VX2000S'"},
            {842150998, "V422"}, //'Vitec Multimedia YUV 4:2:2 as for UYVY but with different component
                                 //ordering within the u_int32 macropixel'"},
            {892679766, "V655"}, //'Vitec Multimedia 16 bit YUV 4:2:2 (6:5:5) format'"},
            {827474774, "VCR1"}, //'ATI VCR 1.0'"},
            {844251990, "VCR2"}, //'ATI VCR 2.0 (MPEG YV12)'"},
            {861029206, "VCR3"}, //'ATI VCR 3.0'"},
            {877806422, "VCR4"}, //'ATI VCR 4.0'"},
            {894583638, "VCR5"}, //'ATI VCR 5.0'"},
            {911360854, "VCR6"}, //'ATI VCR 6.0'"},
            {928138070, "VCR7"}, //'ATI VCR 7.0'"},
            {944915286, "VCR8"}, //'ATI VCR 8.0'"},
            {961692502, "VCR9"}, //'ATI VCR 9.0'"},
            {1413694550, "VDCT"}, //'Video Maker Pro DIB'"},
            {1297040470, "VDOM"}, //'VDOnet VDOWave'"},
            {1464812630, "VDOW"}, //'VDOLive (H.263)'"},
            {1515471958, "VDTZ"}, //'Darim Vision VideoTizer YUV'"},
            {1481656150, "VGPX"}, //'Alaris VGPixel Codec'"},
            {1396984150, "VIDS"}, //'Vitec Multimedia YUV 4:2:2 codec'"},
            {1346783574, "VIFP"}, //'Virtual Frame API codec (VFAPI)'"},
            {827738454, "VIV1"}, //'Vivo H.263'"},
            {844515670, "VIV2"}, //'Vivo H.263'"},
            {1331054934, "VIVO"}, //'Vivo H.263'"},
            {1280854358, "VIXL"}, //'miroVideo XL'"},
            {827739222, "VLV1"}, //'VideoLogic codec'"},
            {808669270, "VP30"}, //'On2 (ex Duck TrueMotion) VP3'"},
            {825446486, "VP31"}, //'On2 (ex Duck TrueMotion) VP3'"},
            {1261525078, "VX1K"}, //'Lucent VX1000S Video Codec'"},
            {1261590614, "VX2K"}, //'Lucent VX2000S Video Codec'"},
            {1347639382, "VXSP"}, //'Lucent VX1000SP Video Codec'"},
            {1129726551, "WBVC"}, //'Winbond W9960 codec'"},
            {1296123991, "WHAM"}, //'Microsoft Video 1'"},
            {1481525591, "WINX"}, //'Winnov Software Compression'"},
            {1196444247, "WJPG"}, //'Winbond JPEG'"},
            {827739479, "WMV1"}, //'Windows Media Video 7'"},
            {844516695, "WMV2"}, //'Windows Media Video 8'"},
            {861293911, "WMV3"}, //'Windows Media Video 9'"},
            {827739735, "WNV1"}, //'WinNow Videum Hardware Compression'"},
            {859189848, "X263"}, //'Xirlink H.263'"},
            {810962008, "XLV0"}, //'NetXL Inc. XL Video Decoder'"},
            {1196445016, "XMPG"}, //'XING MPEG (I frame only)'"},
            {1145656920, "XVID"}, //'XviD MPEG-4 codec'"},
            {825307737, "Y211"}, //'Packed YUV format with Y sampled at every second pixel across each line
                                 //and U and V sampled at every fourth pixel'"},
            {825308249, "Y411"}, //'YUV 4:1:1 Packed'"},
            {1110520921, "Y41B"}, //'YUV 4:1:1 Planar'"},
            {1345401945, "Y41P"}, //'Conexant (ex Brooktree) YUV 4:1:1 Raw'"},
            {1412510809, "Y41T"}, //'Format as for Y41P but the lsb of each Y component is used to signal pixel transparency'"},
            {842151001, "Y422"}, //'Direct copy of UYVY as used by ADS Technologies Pyro WebCam firewire camera'"},
            {1110586457, "Y42B"}, //'YUV 4:2:2 Planar'"},
            {1412576345, "Y42T"}, //'Format as for UYVY but the lsb of each Y component is used to signal pixel transparency'"},
            {808466521, "Y800"}, //'Simple grayscale video'"},
            {536885337, "Y8  "}, //'Simple grayscale video'"},
            {842089305, "YC12"}, //'Intel YUV12 Codec'"},
            {1447974233, "YUNV"}, //'A direct copy of YUY2 registered by NVidia to work around problems in some old codecs
                                  //which did not like hardware which offered more than 2 YUY2 surfaces'"},
            {945182041, "YUV8"}, //'Winnov Caviar YUV8'"},
            {961959257, "YUV9"}, //'Intel YUV9'"},
            {1347835225, "YUVP"}, //'YCbCr 4:2:2 extended precision 10-bits per component in Y0U0Y1V0 order'"},
            {844715353, "YUY2"}, //'YUV 4:2:2 as for UYVY but with different component ordering within the u_int32 macropixel'"},
            {1448695129, "YUYV"}, //'Canopus YUV format'"},
            {842094169, "YV12"}, //'ATI YVU12 4:2:0 Planar'"},
            {961893977, "YVU9"}, //'Brooktree YVU9 Raw (YVU9 Planar)'"},
            {1431918169, "YVYU"}, //'YUV 4:2:2 as for UYVY but with different component ordering within the u_int32 macropixel'"},
            {1112099930, "ZLIB"}, //'Lossless codec (ZIP compression)'"},
            {1195724890, "ZPEG"} //'Metheus Video Zipper'"}
        };

        public override string ToString()
        {
            return $"{Width}x{Height} {Fps} {Format}";
        }
    }
}