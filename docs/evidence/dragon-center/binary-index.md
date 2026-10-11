# 二进制清单

来源与层级见主报告；这是每层提取树的完整 PE 文件索引，不等于安装后实际全部加载。非 PE 配置、字节大小、原生 imports/exports 与托管 AssemblyRef 详见三个 inventory JSON。PE Machine `0x14c` + CLR `0x1` 通常表示 IL-only AnyCPU，不应仅因 PE32 把它写成 x86-only。`0x20003` 含 32BITREQUIRED。

| 提取层/相对路径 | FileVersion | 类型/机器/CLR标志 | 程序集/版本 | SHA-256 |
| --- | --- | --- | --- | --- |
| ui/app/DCv2/API_LiveUpdate_x64.dll | 1.0.0.44 | native-PE; 0x8664; CLR — | — | `96f05a50b7ada924177e6c5ca2318d8642873dc903083a353125aa564802ea97` |
| ui/app/DCv2/BrightnessDetectLibrary.dll | 1.0.0.02 | managed-PE; 0x8664; CLR 0x1 | BrightnessDetectLibrary 1.0.0.2 | `3a59fa22a408a8e7a29712610741269d950653a23a9c00dc85c6b4afefbb3c8b` |
| ui/app/DCv2/CMSILedDll.dll | 未声明 | native-PE; 0x8664; CLR — | — | `90cf3cc4cc532568abdbd3ed6450c0984eb8f1f7671bf5aac9cba077b84fefda` |
| ui/app/DCv2/CMonitorControlLibrary.dll | 2.0.0.06 | managed-PE; 0x14c; CLR 0x1 | CMonitorControlLibrary 2.0.0.6 | `2db343566d4037409651dffe0446efb862e12d6bdccac0fb5424925123fd92ce` |
| ui/app/DCv2/CS_CommonAPI.dll | 2.0.0.39 | managed-PE; 0x14c; CLR 0x1 | CS_CommonAPI 2.0.0.39 | `18564ea5f14e8627ab89037e865cedea5c2f7f7776ac0528a823adf691769937` |
| ui/app/DCv2/CoreAudioApi.dll | 2.0.0.0 | managed-PE; 0x14c; CLR 0x1 | CoreAudioApi 2.0.0.0 | `d324ccdd2b786e5b710a39984b2eec8cb95689067333103369496427ee5df9d4` |
| ui/app/DCv2/DCv2.exe | 2.0.156.0 | managed-PE; 0x8664; CLR 0x1 | DCv2 2.0.156.0 | `b60811864bff3edf344660b424d21cf86a62347b75a2eabba85326aebbdf07ce` |
| ui/app/DCv2/DCv2_Startup.exe | 1.0.0.0 | managed-PE; 0x14c; CLR 0x20003 | DCv2_Startup 1.0.0.0 | `5240b183960e522da00cf2d50968045c212e37e56a19880b486b8e6493ce10db` |
| ui/app/DCv2/Device/GH70/XearAudio.dll | 1.0.0.04 | native-PE; 0x8664; CLR — | — | `8364f5341ba1066b191c4e0d45e929ee12a9a123b21f9c74a364a38f4b472dd0` |
| ui/app/DCv2/Device/GH70/XearSDK/x64/vista/osConfLib.dll | 1, 5, 0, 40 | native-PE; 0x8664; CLR — | — | `db870d26b4be095fe59f788d33fba13febe062012469ed8418516bc393fe66a8` |
| ui/app/DCv2/Device/GK7080/MsiGKbApi.dll | 1.0.0.4 | native-PE; 0x8664; CLR — | — | `e49f135539af6b6994d303a27c8c0b2fb183296bb6ad96a08681af964c064047` |
| ui/app/DCv2/Device/GM08/MsiGM08.dll | 1.0.0.02 | native-PE; 0x8664; CLR — | — | `6a335d7ac4e02ee15523f34bfce4eab0f9af404aa233c62a1278adfa21a69ff2` |
| ui/app/DCv2/Device/GM20Elite/PXICtrl.dll | 1.0.0.8 | native-PE; 0x8664; CLR — | — | `d95e83f1aaeaf958e034b89adebdff81051613fc840b5246af424ecd203afa1e` |
| ui/app/DCv2/Device/GM6070/IcGM60.dll | 1.0.0.21 | native-PE; 0x8664; CLR — | — | `c968b99a1ab65e389e887de68adbee8d2cb4cee47ca5e1979c597a4ba55ddbf6` |
| ui/app/DCv2/Device/GM6070/IcGM70ND.dll | 1.0.18.48 | native-PE; 0x8664; CLR — | — | `0257263733a85bd5e90e6d50eb9b61f5dd1a7a4f9f56d8b0735e49cde1fa0e4b` |
| ui/app/DCv2/Device/GM6070/IcGM70NDRx32.dll | 1.3.5.1 | native-PE; 0x14c; CLR — | — | `41a71669a41c43840ccfa09f0721710ff340f5a4a38a37c27d4a1fb8bae74f58` |
| ui/app/DCv2/Device/GM6070/IcGM70TI.dll | 1.12.17.28 | native-PE; 0x8664; CLR — | — | `de2d563edb2e8685104205c7f54c81182bd367004ba73083c97ff49cfdd06b28` |
| ui/app/DCv2/Device/GM6070/IcMSIDll.dll | 1.3.5.1 | native-PE; 0x8664; CLR — | — | `765462ed857125c7fef4aa32aa390ff9b9c6e5addb8d7dd047e42e58f924bbd0` |
| ui/app/DCv2/Device/MsiHid_x64.dll | 1.0.0.10 | native-PE; 0x8664; CLR — | — | `2890fc26db0ca2902dca6a241b77e52be8f1a996c9168bfd99b20455c1d8fe3a` |
| ui/app/DCv2/DirectShowLib-2005.dll | 2.1.0.0 | managed-PE; 0x14c; CLR 0x9 | DirectShowLib-2005 2.1.0.0 | `bbcbb170242d9ff1b56680a80b1f8755df1135f9c714535ff3b3f575442f38dc` |
| ui/app/DCv2/EntityFramework.SqlServer.dll | 6.2.61023.0 | managed-PE; 0x14c; CLR 0x9 | EntityFramework.SqlServer 6.0.0.0 | `e0e126c3b8a7ba9501d03f99d54a5cbbd174bcecb6170098ab4cc1219f27f933` |
| ui/app/DCv2/EntityFramework.dll | 6.2.61023.0 | managed-PE; 0x14c; CLR 0x9 | EntityFramework 6.0.0.0 | `9c20fb2ad86a760b0c6aebd9301c9009891d02ae14720a93926c0b535b8ad09b` |
| ui/app/DCv2/FaceRecognitionLibrary.dll | 1.0.0.03 | managed-PE; 0x14c; CLR 0x1 | FaceRecognitionLibrary 1.0.0.3 | `8cf3d1c6c71709b39c8621cc79e8fba108ffdb2b24033cac09624f4003600243` |
| ui/app/DCv2/Facebook.dll | 7.0.6 | managed-PE; 0x14c; CLR 0x9 | Facebook 6.0.10.0 | `8327ff865061b49582968759e00480ac198509c896fad727ca27061af9394d79` |
| ui/app/DCv2/GamingOSDLiteLibrary.dll | 0.0.0.1 | managed-PE; 0x14c; CLR 0x1 | GamingOSDLiteLibrary 0.0.0.1 | `140367864dba5cf6d00590db026579014c7797485c37046a6c14834dbfcb6288` |
| ui/app/DCv2/HIDCtrl.dll | 未声明 | managed-PE; 0x8664; CLR 0x10 | HIDCtrl 1.0.7555.28291 | `52e4fe1290aec9c6093a8546c1f0c099f13fb63ac0ec6fd6e87012ad8a38fcd3` |
| ui/app/DCv2/MSILedDll.dll | 未声明 | native-PE; 0x8664; CLR — | — | `90cf3cc4cc532568abdbd3ed6450c0984eb8f1f7671bf5aac9cba077b84fefda` |
| ui/app/DCv2/Microsoft.Expression.Drawing.dll | 3.0.40218.0 | managed-PE; 0x14c; CLR 0x9 | Microsoft.Expression.Drawing 4.5.0.0 | `1d69eaf538008e0fe1a7eb2ce0124a49b95c491797749640c8351ed4643f5c97` |
| ui/app/DCv2/Microsoft.Win32.TaskScheduler.dll | 2.4.2.0 | managed-PE; 0x14c; CLR 0x9 | Microsoft.Win32.TaskScheduler 2.4.2.0 | `31b3be692639116f163c5aa594297d97759bccbc297a6e29e649281a8f6dd3ab` |
| ui/app/DCv2/Microsoft.WindowsAPICodePack.Shell.dll | 1.1.0.0 | managed-PE; 0x14c; CLR 0x1 | Microsoft.WindowsAPICodePack.Shell 1.1.0.0 | `e6fa9d9e34ff3bf63ce782654b14e4b54a3abd1022c87bc099032c2948157672` |
| ui/app/DCv2/Microsoft.WindowsAPICodePack.dll | 1.1.2.0 | managed-PE; 0x14c; CLR 0x1 | Microsoft.WindowsAPICodePack 1.1.2.0 | `808e96f59e7dd2212eace049079d25545f6c9c3f05244ec9cdc539fda18d34d6` |
| ui/app/DCv2/MonitorControlLibrary.dll | 1.0.0.11 | managed-PE; 0x8664; CLR 0x1 | MonitorControlLibrary 1.0.0.11 | `554ac21eee6241d9327a593798beefeaf90339ed2c2953f3bab894b71862d2ac` |
| ui/app/DCv2/MonitorControlLibrary_MyDevice.dll | 1.0.0.09 | managed-PE; 0x8664; CLR 0x1 | MonitorControlLibrary_MyDevice 1.0.0.9 | `bc3115e2d815f5c4bcb09c875162407c9d555276bea2316fc0ec448cdf852346` |
| ui/app/DCv2/Mystic_Light/IcGM60.dll | 1.0.0.21 | native-PE; 0x8664; CLR — | — | `c968b99a1ab65e389e887de68adbee8d2cb4cee47ca5e1979c597a4ba55ddbf6` |
| ui/app/DCv2/Mystic_Light/IcGM70ND.dll | 1.0.18.48 | native-PE; 0x8664; CLR — | — | `0257263733a85bd5e90e6d50eb9b61f5dd1a7a4f9f56d8b0735e49cde1fa0e4b` |
| ui/app/DCv2/Mystic_Light/IcGM70NDRx32.dll | 1.3.5.1 | native-PE; 0x14c; CLR — | — | `41a71669a41c43840ccfa09f0721710ff340f5a4a38a37c27d4a1fb8bae74f58` |
| ui/app/DCv2/Mystic_Light/IcGM70TI.dll | 1.12.17.28 | native-PE; 0x8664; CLR — | — | `de2d563edb2e8685104205c7f54c81182bd367004ba73083c97ff49cfdd06b28` |
| ui/app/DCv2/Mystic_Light/IcMSIDll.dll | 1.3.5.1 | native-PE; 0x8664; CLR — | — | `765462ed857125c7fef4aa32aa390ff9b9c6e5addb8d7dd047e42e58f924bbd0` |
| ui/app/DCv2/Mystic_Light/MsiGKbApi_x64.dll | 1.0.0.4 | native-PE; 0x8664; CLR — | — | `e49f135539af6b6994d303a27c8c0b2fb183296bb6ad96a08681af964c064047` |
| ui/app/DCv2/Mystic_Light/MsiHid_x64.dll | 1.0.0.10 | native-PE; 0x8664; CLR — | — | `2890fc26db0ca2902dca6a241b77e52be8f1a996c9168bfd99b20455c1d8fe3a` |
| ui/app/DCv2/NAudio.dll | 1.10.0.0 | managed-PE; 0x14c; CLR 0x1 | NAudio 1.10.0.0 | `bc4bacc3b8b28d898f1671b79f216cca439f95eb60cd32d3e3ecafbecac42780` |
| ui/app/DCv2/NahimicAPI.dll | 1.1.7.0 | native-PE; 0x8664; CLR — | — | `7434e6e74c537e097b93bc5291bdf89905fed240acbe6ecf969f15e7c8e112ce` |
| ui/app/DCv2/NamedPipeClientLib.dll | 1.1.2103.1701 | managed-PE; 0x14c; CLR 0x1 | NamedPipeClientLib 1.1.2103.1701 | `b1e5ed2922439b0897c0f2dc5cd4d47c0becf81d35bac690d39fa17b4bfd55b8` |
| ui/app/DCv2/Newtonsoft.Json.dll | 12.0.3.23909 | managed-PE; 0x14c; CLR 0x9 | Newtonsoft.Json 12.0.0.0 | `9000cfcb27d21ff1d9f7196b678ecc93be98af2a14254c03a554318891e6cb23` |
| ui/app/DCv2/OpenCvSharp.Blob.dll | 1.0.0.0 | managed-PE; 0x14c; CLR 0x9 | OpenCvSharp.Blob 1.0.0.0 | `90347fed969956df2da62bee8083cef95ee31614322dc91ffafd9c00dc3482a5` |
| ui/app/DCv2/OpenCvSharp.Extensions.dll | 1.0.0.0 | managed-PE; 0x14c; CLR 0x9 | OpenCvSharp.Extensions 1.0.0.0 | `0b74d052af47277e10671d2f38affc1d3d87e00568f8164e5638f90ab1fb2ccb` |
| ui/app/DCv2/OpenCvSharp.UserInterface.dll | 1.0.0.0 | managed-PE; 0x14c; CLR 0x9 | OpenCvSharp.UserInterface 1.0.0.0 | `6e00974a3316b6b23d435735efcc40fab3a62128d33733dc774494b976c27080` |
| ui/app/DCv2/OpenCvSharp.dll | 1.0.0.0 | managed-PE; 0x14c; CLR 0x9 | OpenCvSharp 1.0.0.0 | `c0eedec313cd9845cd7c1d601e33e524c48595638b61426bbb8d0b651e869ff6` |
| ui/app/DCv2/PXICtrl.dll | 1.0.0.8 | native-PE; 0x8664; CLR — | — | `d95e83f1aaeaf958e034b89adebdff81051613fc840b5246af424ecd203afa1e` |
| ui/app/DCv2/Package/MSI SDK.exe | 2.2026.0821.01       | native-PE; 0x14c; CLR — | — | `e6013ef092978a8f635be03a270d05662436adf415a8720c226d9fd3a378b7aa` |
| ui/app/DCv2/ParamXer.dll | 1.0.4.0 | managed-PE; 0x8664; CLR 0x1 | ParamXer 1.0.4.0 | `c3b65db6986806318c4372adf57d490c6afd4c1c36852f0085bf3ddc77b25be0` |
| ui/app/DCv2/SQLite.Interop.dll | 1.0.110.0 | native-PE; 0x8664; CLR — | — | `d0a2eff3dcdd75d1ea8f716c321e151667673d42f1de3fcc860066365a5ae3be` |
| ui/app/DCv2/SecondScreenLibrary.dll | 1.0.0.02 | managed-PE; 0x8664; CLR 0x1 | SecondScreenLibrary 1.0.0.2 | `5d5fcc3627d037280e56edd4a90416e724898242767f8b316c740eee92e5bc07` |
| ui/app/DCv2/Stepon.FaceRecognization.dll | 1.0.0.01 | managed-PE; 0x8664; CLR 0x1 | Stepon.FaceRecognization 1.0.0.1 | `2a6472bf9ea5143e5f66d7d68bbe9d9c560f7b837ea55c91767ca4d4fa284e47` |
| ui/app/DCv2/System.Data.SQLite.EF6.dll | 1.0.110.0 | managed-PE; 0x14c; CLR 0x9 | System.Data.SQLite.EF6 1.0.110.0 | `b4118b039b8abf7c3ea7aa1936261a36509a561a90deb4beecbcf5e08678b07b` |
| ui/app/DCv2/System.Data.SQLite.Linq.dll | 1.0.110.0 | managed-PE; 0x14c; CLR 0x9 | System.Data.SQLite.Linq 1.0.110.0 | `542a586ccadc0038724196bb6acb9015f86c5d58307abf71c7aea975f45f3f9d` |
| ui/app/DCv2/System.Data.SQLite.dll | 1.0.110.0 | managed-PE; 0x14c; CLR 0x9 | System.Data.SQLite 1.0.110.0 | `3d9667e9068bb555f5b9bccbac1875a6eb0e9e00655de369be4a64052293ebcb` |
| ui/app/DCv2/System.Windows.Interactivity.dll | 3.0.40218.0 | managed-PE; 0x14c; CLR 0x9 | System.Windows.Interactivity 4.5.0.0 | `93fbc59e4880afc9f136c3ac0976ada7f3faa7cacedce5c824b337cbca9d2ebf` |
| ui/app/DCv2/UC_Base Module.dll | 1.0.0.79 | managed-PE; 0x8664; CLR 0x1 | UC_Base Module 1.0.0.79 | `3619a3712ec7fcb8bcbd23f7ed0733f631a08a461fd5d76d2c2cc022d34ba371` |
| ui/app/DCv2/UC_Burn Recovery.dll | 1.0.2110.0601 | managed-PE; 0x14c; CLR 0x1 | UC_NB_Burn Recovery 1.0.2110.601 | `b38a23856893e9ef3c09ff6deb9ba604cf4478e2c08e95b1f867dd8810c18124` |
| ui/app/DCv2/UC_DPC_Latency_Tuner.dll | 1.0.0.13 | managed-PE; 0x14c; CLR 0x1 | UC_DPC_Latency_Tuner 1.0.0.13 | `48db5a02d27530382742464635ccec5d9d71b6cc7588d2aa301e584d88a4143e` |
| ui/app/DCv2/UC_Device.dll | 1.0.0.72 | managed-PE; 0x14c; CLR 0x1 | UC_Device 1.0.0.72 | `c74a3a51bb1a189e8a12322baa72120d9864fee40a7bf01d674aedae530b3486` |
| ui/app/DCv2/UC_Duet.dll | 1.0.0.14 | managed-PE; 0x14c; CLR 0x1 | UC_Duet 1.0.0.14 | `c1df47f62b8fa54bc97e2d6f70fff8ff9819f43fe315334a2d92c77b76d308ee` |
| ui/app/DCv2/UC_Game_Summary.dll | 1.0.0.20 | managed-PE; 0x8664; CLR 0x1 | UC_Game_Summary 1.0.0.20 | `71ffc5cc9f3d416af007a47ba54c865d753f828a39d00e55d556a37b189732db` |
| ui/app/DCv2/UC_Gaming_Hotkey.dll | 1.0.0.11 | managed-PE; 0x8664; CLR 0x1 | UC_Gaming_Hotkey 1.0.0.11 | `2acd9dfcc4b6decce0b2f1a5162dd7d3475ce4e5196c0bdb5ef6c2e4fee972d3` |
| ui/app/DCv2/UC_Gaming_OSD.dll | 1.0.0.31 | managed-PE; 0x8664; CLR 0x1 | UC_Gaming_OSD 1.0.0.31 | `74798fc13618267525ebcf0f7dc8264aff045ecbdcc7d2b17a77f39bb24a25b3` |
| ui/app/DCv2/UC_Graphics Fan Tool.dll | 1.0.0.17 | managed-PE; 0x14c; CLR 0x1 | UC_Graphics Fan Tool 1.0.0.17 | `72106a0dc49d656f3519c1ba1f74a29a4cc76a9b654fbd33d6904bc67b10798b` |
| ui/app/DCv2/UC_LanManager.dll | 1.0.0.20 | managed-PE; 0x14c; CLR 0x1 | UC_LanManager 1.0.0.20 | `62455ddf97fa254a7f7b4e06f192679448a7cbcc8eab62813cf777da041fe68d` |
| ui/app/DCv2/UC_My Device.dll | 1.0.0.66 | managed-PE; 0x14c; CLR 0x1 | UC_My Device 1.0.0.66 | `4ca76059578323512dcb1c9d055fc92dcd8e9674c89369bf514595b7549f0c2d` |
| ui/app/DCv2/UC_Mystic_Light.dll | 1.0.1.34 | managed-PE; 0x14c; CLR 0x1 | UC_Mystic_Light 1.0.1.34 | `69ab1d7136575074e69468945e7051c4431f7669c8d5ba6f15616812295fa98e` |
| ui/app/DCv2/UC_NB_Base Module.dll | 1.0.2402.1901 | managed-PE; 0x14c; CLR 0x1 | UC_NB_Base Module 1.0.2402.1901 | `c11007bc5bb73b9b16ea5d60b331257cbbe7a8e20ade75f611b54810298c72e7` |
| ui/app/DCv2/UC_NB_Battery.dll | 1.0.2402.0101 | managed-PE; 0x14c; CLR 0x1 | UC_NB_Battery 1.0.2402.101 | `ecb508b9473b1d3a92fa01fcdae3b71822750a30f1dc2debed5db4a708dad46d` |
| ui/app/DCv2/UC_NB_Burn Recovery.dll | 1.0.2110.0601 | managed-PE; 0x14c; CLR 0x1 | UC_NB_Burn Recovery 1.0.2110.601 | `27b5cb1ab163aab3386570ff5c8388eb5831d2948f08cd8cb5fed6dc162f9e9b` |
| ui/app/DCv2/UC_NB_Knowledge Base.dll | 1.0.2108.0501 | managed-PE; 0x8664; CLR 0x1 | UC_NB_Knowledge Base 1.0.2108.501 | `75eafa764ece6c33eeb45d815de5d7b28063b9a208d811cd7ca98f286c2bc74d` |
| ui/app/DCv2/UC_NB_MicrosoftAPP.dll | 1.0.2401.3001 | managed-PE; 0x8664; CLR 0x1 | UC_NB_MicrosoftAPP 1.0.2401.3001 | `2621fbba468382ca4ff7a1b66fe9502dc0ee23c542912267d2ed1d7836129620` |
| ui/app/DCv2/UC_NB_System Info.dll | 1.0.2105.0301 | managed-PE; 0x8664; CLR 0x1 | UC_NB_System Info 1.0.2105.301 | `7d75e88f370f71607402da172e331f9d2ec3f0853e77bc047d6095ceeb8e2f35` |
| ui/app/DCv2/UC_NB_System Monitor.dll | 1.0.2104.2801 | managed-PE; 0x14c; CLR 0x1 | UC_NB_System Monitor 1.0.2104.2801 | `dcdef1ac5dc91dda67da7e23b5fb04ce725d414f948e29d621334977b9f01c69` |
| ui/app/DCv2/UC_NB_True Color.dll | 1.0.2104.2801 | managed-PE; 0x14c; CLR 0x1 | UC_NB_True Color 1.0.2104.2801 | `686fc785f8fa62a04a021eb7a1467951bdffcdeb0a12073cca84d62b13bff443` |
| ui/app/DCv2/UC_NB_Update.dll | 1.0.2401.3001 | managed-PE; 0x8664; CLR 0x1 | UC_NB_Update 1.0.2401.3001 | `77a0d846f2d2c749c4413ca6dd0e3ac34db345b891c0b7ea4ef65e71f4dccb13` |
| ui/app/DCv2/UC_Smart Tool.dll | 1.0.0.12 | managed-PE; 0x14c; CLR 0x1 | UC_Smart Tool 1.0.0.12 | `1dbad25dc64f6468e9f2db9df9960c6d30d9bb12337120b944f75a4d8dec23ba` |
| ui/app/DCv2/UC_Sound Tune.dll | 1.0.0.34 | managed-PE; 0x8664; CLR 0x1 | UC_Sound Tune 1.0.0.34 | `b3c2859a313695e367e91d02400804b7cc5d16d56c7021a428ca24490ea141d0` |
| ui/app/DCv2/UC_Speed Up.dll | 1.0.0.06 | managed-PE; 0x14c; CLR 0x1 | UC_Speed Up 1.0.0.6 | `1d5dda78999e904120f0a7495c4e4615233323b6f898759fc4ee555548d51d9b` |
| ui/app/DCv2/UC_Super_Charger.dll | 1.0.0.07 | managed-PE; 0x14c; CLR 0x1 | UC_Super_Charger 1.0.0.7 | `4599813cc1c1367d10b2b338f61f6fe9e9b7f41fbc555dc71e9cd7a27eb1f56e` |
| ui/app/DCv2/UC_Support.dll | 1.0.0.33 | managed-PE; 0x8664; CLR 0x1 | UC_Support 1.0.0.33 | `c7ffe846287997991f4ec7e0102c7125a8bb7050ff20f3250021ff3ccd527e1e` |
| ui/app/DCv2/UC_True Color.dll | 2.0.0.22 | managed-PE; 0x14c; CLR 0x1 | UC_True Color 2.0.0.22 | `81b4f568cf8b872fd7bd2891cb40c7edd44ceac4e97bdbb0a0a148c7258e85b9` |
| ui/app/DCv2/UC_Voice Boost.dll | 1.0.0.08 | managed-PE; 0x14c; CLR 0x1 | UC_Voice Boost 1.0.0.8 | `41ffc92b29c02f599ea57534b36608939353d400771606cbea208a7754f51581` |
| ui/app/DCv2/VideoCapLib.dll | 1.0.0.01 | managed-PE; 0x8664; CLR 0x1 | VideoCapLib 1.0.0.1 | `5872186541a84da0fada2c2ac32b1e5b52543d46cae7a9913fdf05b8bef3e6f0` |
| ui/app/DCv2/dll/x64/OpenCvSharpExtern.dll | 未声明 | native-PE; 0x8664; CLR — | — | `3bd87ddbf8f34bf099f9e7663ec29c3b14eb25f099d4106c812f6dd4382f016f` |
| ui/app/DCv2/dll/x64/opencv_ffmpeg400_64.dll | 2018.11.0 | native-PE; 0x8664; CLR — | — | `f68b8a8f2dbfc3036e0065d6cb78dad121bf4f5dd9aeed86b0560c1fd0d52bf3` |
| ui/app/DCv2/libs/libarcsoft_fsdk_face_detection.dll | 未声明 | native-PE; 0x8664; CLR — | — | `de5e571706d42b656d8381411b035f5404ee41e6962f3b8224e1086e50ced20e` |
| ui/app/DCv2/libs/libarcsoft_fsdk_face_recognition.dll | 未声明 | native-PE; 0x8664; CLR — | — | `2a32a3d98503d3843b735e2089f3f86ff6cfa5c82b6c27580840a5005160498b` |
| ui/app/DCv2/libs/libarcsoft_fsdk_face_tracking.dll | 未声明 | native-PE; 0x8664; CLR — | — | `f4e0ba233b0f936e7f81cb4b1184a9ad370b9af5abb161a9618b8cecde452aa0` |
| ui/app/DCv2/x64/SQLite.Interop.dll | 1.0.110.0 | native-PE; 0x8664; CLR — | — | `d0a2eff3dcdd75d1ea8f716c321e151667673d42f1de3fcc860066365a5ae3be` |
| ui/app/DCv2/x86/SQLite.Interop.dll | 1.0.110.0 | native-PE; 0x14c; CLR — | — | `12882fe4ce29c18f2ed7659a889d5b53796b5208b62522a708111b10f71509b2` |
| ui/app/WinMetadata/Windows.winmd | 未声明 | managed-PE; 0x14c; CLR 0x1 | Windows 255.255.255.255 | `4aae59a25f02cd69b825f94085fb9f13ff2ffbd25a37b62eb31aec527ffe2f1c` |
| sdk/app/API_Kernel.dll | 2.0.0.21 | native-PE; 0x14c; CLR — | — | `67a10cacc51800d0061d5ac2ae96030b5b6b7cec150389699619fcd03a61b72f` |
| sdk/app/CS_CommonAPI.dll | 2.0.0.40 | managed-PE; 0x14c; CLR 0x1 | CS_CommonAPI 2.0.0.40 | `498740a6695a3aee6df5d89877d094e0ee35b52b057dd882eaf6cd430873c881` |
| sdk/app/MSI.CentralServer.exe | 2.0.0.35 | managed-PE; 0x14c; CLR 0x20003 | MSI.CentralServer 2.0.0.35 | `7b525c3f6804184d43662749daf271ff6bfd0db5b1db56ebd82bf794dc4a9786` |
| sdk/app/MSI.NotifyServer.exe | 2.0.0.35 | managed-PE; 0x8664; CLR 0x1 | MSI.NotifyServer 2.0.0.35 | `9af691e9e40eb2a815a3dc7b86336d624b50f082dcb89a11dab5ff7d892eca57` |
| sdk/app/MSI_Central_Service.exe | 1.0.0.49 | native-PE; 0x14c; CLR — | — | `f2b6406e7476f711d2e6caf93f7be0cbc69d5eebee87c3878dbd9fceab618bcc` |
| sdk/app/NB_Support.dll | 1.0.2110.1301 | managed-PE; 0x14c; CLR 0x1 | NB_Support 1.0.2110.1301 | `2720f4c9ee1331d35962f5bc35d589dbc3fac1a8eb5a782589fc224de34567f7` |
| sdk/tmp/PrePackage/Base Module_1.0.0.48.exe | 1.0.0.48             | native-PE; 0x14c; CLR — | — | `271fe32711a99cdd11839e7fcc5bc97616f719b3a29d4333bc8f5eb8359cff66` |
| sdk/tmp/PrePackage/Device_1.0.0.44.exe | 1.0.0.44             | native-PE; 0x14c; CLR — | — | `9a03fc2a519d97b99b0b635d943a87dce431dc6899a5d5348f837910d1963855` |
| sdk/tmp/PrePackage/Engine Lib_1.0.0.22.exe | 1.0.0.22             | native-PE; 0x14c; CLR — | — | `e48db09ecd2ac708f52132dd236f55a06eaf9aacfeafedc2dd38ac0217b56f00` |
| sdk/tmp/PrePackage/Engine_1.0.0.89.exe | 1.0.0.89             | native-PE; 0x14c; CLR — | — | `66064a9056b52a746a4145d38304e1dc18c75ad84a7d365897ea19f4843c4324` |
| sdk/tmp/PrePackage/Export/Setup.exe | 1.0.2411.1201        | native-PE; 0x14c; CLR — | — | `54860b1481f0b64e63629bef93f85a16884d625910fdd2b14bc946e0b565f3b8` |
| sdk/tmp/PrePackage/Export/UninstallProcedure.exe | 1.0.2310.2301 | managed-PE; 0x14c; CLR 0x1 | UninstallProcedure 1.0.2310.2301 | `4f9a19cda1e8c176c556f1605ef0333293be5be81625effbda811ffc1c1373e1` |
| sdk/tmp/PrePackage/Export/xtu-setup-exe.exe | 6.5.2.38 | native-PE; 0x14c; CLR — | — | `dd3a2e6fc62990a99d8eb5ecb1d8b44c66f83c38b0a9bde937e076b4f2be1462` |
| sdk/tmp/PrePackage/Game Summary_1.0.0.43.exe | 1.0.0.43             | native-PE; 0x14c; CLR — | — | `c8da4e8a9a585267de405e363bd27993ab1f69a5918b734aa1122dc8fa8c71e5` |
| sdk/tmp/PrePackage/Graphics Fan Tool_1.0.0.02.exe | 1.0.0.02             | native-PE; 0x14c; CLR — | — | `dfa5d7b6e22effb3b44fed80e4837a93fd3dfc506e707379f48ec87716b4b066` |
| sdk/tmp/PrePackage/Lan Manager_1.0.0.68.exe | 1.0.0.68             | native-PE; 0x14c; CLR — | — | `13a6fb8f1a043ee3ffb617c031473ed56ae98819059b4000699fe659a25e0d3e` |
| sdk/tmp/PrePackage/MSI_NBFoundation_Service.exe | 1.0.2411.1201 | managed-PE; 0x14c; CLR 0x1 | MSI_NBFoundation_Service 1.0.2411.1201 | `5c52905c8b349b9883c84351e2fdcfb45ad49d264a03f731b0ea2c4ca83276c0` |
| sdk/tmp/PrePackage/My Device_1.0.0.11.exe | 1.0.0.11             | native-PE; 0x14c; CLR — | — | `7e3423d2e2b1f48294c0fb908139ad8adaf8f60c718a60c3ae65b8387a27b910` |
| sdk/tmp/PrePackage/Mystic Light_1.0.1.45.exe | 1.0.1.45             | native-PE; 0x14c; CLR — | — | `6c6d939cecdeae885f3935bcde4ca92182b8d980dcb3c2868d92c7e224beb151` |
| sdk/tmp/PrePackage/Nahimic_1.0.0.06.exe | 1.0.0.06             | native-PE; 0x14c; CLR — | — | `1978516b95b73b9003b722953b8440cdb9203c886d27f9d811bebe39cc1844d7` |
| sdk/tmp/PrePackage/Sound Tune_1.0.0.47.exe | 1.0.0.47             | native-PE; 0x14c; CLR — | — | `15ec20f57eb99c5e97e45cadb78ae2de5abb093681bfcd5ff16b6f47f79dd13d` |
| sdk/tmp/PrePackage/Support_1.0.0.71.exe | 1.0.0.71             | native-PE; 0x14c; CLR — | — | `fd57c9fea724319f882cb9392637bef199657b35d8a169e8c8cf3992fc0f8b73` |
| sdk/tmp/PrePackage/True Color_2.0.0.20.exe | 2.0.0.20             | native-PE; 0x14c; CLR — | — | `559fe40499a0b0bab540d831311b82ac8fa42b793b1331b4668f8204af191faa` |
| nbfoundation/app/GInf.dll | 2.0.1908.2701 | native-PE; 0x8664; CLR — | — | `e0675578d0bf298fec13ec7f4d4a0b81081611f4fc18a3dc3da98b19e82d6ef2` |
| nbfoundation/app/Ginf2.dll | 2.0.2011.1601 | native-PE; 0x8664; CLR — | — | `e0a0e110e3061388a8e2e074332f105ad25ca25319e78ec406b1f186bc1d65c9` |
| nbfoundation/app/KernCoreLib64.Sys | 1.0.2411.0401 | native-PE; 0x8664; CLR — | — | `a3c71d0d687f41ebf3970e7e799cd8c4784557ac50d9728b62f4273f9627cffc` |
| nbfoundation/app/KernCoreLib64.dll | 1.0.1807.3101 | native-PE; 0x8664; CLR — | — | `dafc2c87b674e3b7f1fb355064061ad48fb33c79c0eb172390b04e1261e8bf0b` |
| nbfoundation/app/MSIAPService.exe | 1.1.2408.1501 | managed-PE; 0x8664; CLR 0x1 | MSIAPService 1.1.2408.1501 | `52de5b9df1212f3cf040338b1ae92250d1021af9980424baaef9551e427ad982` |
| nbfoundation/app/MSIBatteryCalibration.exe | 1.0.2407.2301 | managed-PE; 0x14c; CLR 0x1 | MSIBatteryCalibration 1.0.2407.2301 | `de4a68e02e6b22ee9b37e11fd4d98f3a29f7ac081ed286ad5917235c73ecd635` |
| nbfoundation/app/MSIWMIACPI2.dll | 1.0.2410.0101 | managed-PE; 0x14c; CLR 0x1 | MSIWMIACPI2 1.0.2410.101 | `1930038a503f8a6cc5824fa479e44c9fc9cf95e0dc60e78caad4b3df76533828` |
| nbfoundation/app/MSI_NBFoundation_Service.exe | 1.0.2411.1201 | managed-PE; 0x14c; CLR 0x1 | MSI_NBFoundation_Service 1.0.2411.1201 | `5c52905c8b349b9883c84351e2fdcfb45ad49d264a03f731b0ea2c4ca83276c0` |
| nbfoundation/app/NamedPipeClientLib.dll | 1.1.2408.0501 | managed-PE; 0x14c; CLR 0x1 | NamedPipeClientLib 1.1.2408.501 | `3bef0ce4026ef03bbfb491ceb550ce85a1ddf0451053c048a2c83a589ad8c38d` |
| nbfoundation/app/Newtonsoft.Json.dll | 12.0.3.23909 | managed-PE; 0x14c; CLR 0x9 | Newtonsoft.Json 12.0.0.0 | `9000cfcb27d21ff1d9f7196b678ecc93be98af2a14254c03a554318891e6cb23` |
| nbfoundation/app/OmApSvcBroker.exe | 1.0.2401.2901 | managed-PE; 0x8664; CLR 0x1 | OmApSvcBroker 1.0.2401.2901 | `8f937e01097cae79f6391a7e1672782b426792d7d4b022bcb40c24d8e33c9dd6` |
| nbfoundation/app/OneDC_Updater.exe | 2.0.2008.2601 | managed-PE; 0x8664; CLR 0x1 | OneDC_Updater 2.0.2008.2601 | `e52e93193093b89da448b9de326f6927f7c96b52f65958d3f40eb6d18ac170d7` |
| nbfoundation/app/ReAgentcCmd.exe | 3.1000.1608.2901 | native-PE; 0x8664; CLR — | — | `f331f4850a84965950ce6a2b839eee7d1f152134bcfdaeeb7432ed8bba214dd1` |
| nbfoundation/app/RemindRegister.exe | 1.0.2102.2201 | managed-PE; 0x14c; CLR 0x20003 | RemindRegister 1.0.2102.2201 | `2425275b6211847a0e20f890a3e463ef6eb8cc92b0f5a71cb85c8d3007598a40` |
| nbfoundation/app/Remind_SSD.exe | 1.0.2104.2201 | managed-PE; 0x14c; CLR 0x1 | Remind_SSD 1.0.2104.2201 | `d452c04650df005fcc51efa42a9086767f5ca0661f4eec4a0707fe9a8197b0cc` |
| nbfoundation/app/ResetUEFIVariable.exe | 1.0.1909.2401 | managed-PE; 0x14c; CLR 0x1 | ResetUEFIVariable 1.0.1909.2401 | `0f73c1d3de8154e92403f108cf76bcd4b91af92a8e14f333f1da81ab16a3044c` |
| nbfoundation/app/Sendevsvc.exe | 1.0.2006.2301 | managed-PE; 0x14c; CLR 0x3 | Sendevsvc 1.0.2006.2301 | `cddfc01c8a186c82bede0a803fa2d6f5abb2df640765778a1124858348dd76a5` |
| nbfoundation/app/ServiceInstall.exe | 1.0.1810.2901 | managed-PE; 0x14c; CLR 0x20003 | ServiceInstall 1.0.1810.2901 | `30d39fc0dfacdabc08e9d9a0cdccb3b7717af8fb2ec8fb5c1685a1bc8fa5cd18` |
| nbfoundation/app/TwiceRestart.exe | 1.0.1806.2201 | managed-PE; 0x14c; CLR 0x20003 | TwiceRestart 1.0.1806.2201 | `a4f35a9a27e4b2c008e5d877f84ef3ff112369fe6c5efc44b21089b8b4806672` |
| nbfoundation/app/UEFIVaribleDll.dll | 未声明 | native-PE; 0x8664; CLR — | — | `cd64d5addbdc274fb56ab6f98addaa119552a9672e2919ea719ba0892928873b` |
| nbfoundation/app/UninstallProcedure.exe | 1.0.2310.2301 | managed-PE; 0x14c; CLR 0x1 | UninstallProcedure 1.0.2310.2301 | `4f9a19cda1e8c176c556f1605ef0333293be5be81625effbda811ffc1c1373e1` |
| nbfoundation/app/VC_redist.x64.exe | 14.11.25325.0 | native-PE; 0x14c; CLR — | — | `7434bf559290cccc3dd3624f10c9e6422cce9927d2231d294114b2f929f0e465` |
| nbfoundation/app/WMILib.dll | 1.0.1904.1701 | native-PE; 0x8664; CLR — | — | `acd8fd46c0fc95c0c33f8c4df1e779771024c4374b023978799312b871b3dd61` |
| nbfoundation/app/gpuControl.exe | 1.2.2011.2401 | managed-PE; 0x14c; CLR 0x1 | gpuControl 1.2.2011.2401 | `f065a27f00fe20342718e2612acdce95069791c19d5ac999efb7eabe0ddbc01d` |
| nbfoundation/app/msiRecovery.exe | 1.1000.2403.601 | native-PE; 0x8664; CLR — | — | `3da36dc714157f5e3708062389cefb869d3580c1eb4f56e84c185fac21a49932` |
| nbfoundation/app/msiapcfg.dll | 10.0.10011.16384 | native-PE; 0x14c; CLR — | — | `cca21fde539c18885684a639eb2aa97b1859eeb8881ff3546f8adb0dd6657645` |
| nbfoundation/pf/MSI/One Dragon Center/7zxa.dll | 19.00 | native-PE; 0x8664; CLR — | — | `a2b1b513fbb7e22840476056834421e9849d80edb30345940a8f8a2276b4dc19` |
| nbfoundation/pf/MSI/One Dragon Center/AInf.dll | 1.0.2108.2501 | native-PE; 0x14c; CLR — | — | `f6138d1ac083258019c76e922feec7ab9c60ea0c17c1aaeef37fb34aedcf7ce4` |
| nbfoundation/pf/MSI/One Dragon Center/Base Module/API_HDR.dll | 1.0.0.11 | managed-PE; 0x14c; CLR 0x1 | API_HDR 1.0.0.11 | `e6f214d170224085300bc6dd609ec255384bee83ce2daaca0b2b24bb3f45989f` |
| nbfoundation/pf/MSI/One Dragon Center/Base Module/API_NB_Base Module.dll | 1.0.2201.2401 | managed-PE; 0x14c; CLR 0x1 | API_NB_Base Module 1.0.2201.2401 | `cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192` |
| nbfoundation/pf/MSI/One Dragon Center/Base Module/Start_HDR.exe | 1.0.0.1 | managed-PE; 0x14c; CLR 0x20003 | Start_HDR 1.0.0.1 | `8c03a660f4b701c7a182212e2b6684d51d81b2691bf8238f1b0c1283486f7c78` |
| nbfoundation/pf/MSI/One Dragon Center/Base Module/Windows.winmd | 未声明 | managed-PE; 0x14c; CLR 0x1 | Windows 255.255.255.255 | `294dae204d192975f29da17d4ff7eb9e47a15d03ae158587f33e1902e372718f` |
| nbfoundation/pf/MSI/One Dragon Center/Battery/API_NB_Battery.dll | 1.0.2109.1601 | managed-PE; 0x14c; CLR 0x1 | API_NB_Battery 1.0.2109.1601 | `db88c39b264beafd8fd409a344316a1ea867316e43fda7ff39c8cc77d778af9e` |
| nbfoundation/pf/MSI/One Dragon Center/Battery/API_SSD Doctor.dll | 1.0.0.6 | native-PE; 0x14c; CLR — | — | `2a954413d587c623163bc003226bf5aec6a941a5134caa194a4b8d1164164e85` |
| nbfoundation/pf/MSI/One Dragon Center/Battery/CS_CommonAPI.dll | 2.0.0.05 | managed-PE; 0x14c; CLR 0x1 | CS_CommonAPI 2.0.0.5 | `094b34e9d0422a3dc9e4023aefb808a88e7fab99d98bec0541b9650752573b45` |
| nbfoundation/pf/MSI/One Dragon Center/Battery/NamedPipeClientLib.dll | 1.1.2103.1701 | managed-PE; 0x14c; CLR 0x1 | NamedPipeClientLib 1.1.2103.1701 | `29ce3e3a0ac831aeadd118316ef5740963bdf60e8516b72c9cd4ae703cbf94c5` |
| nbfoundation/pf/MSI/One Dragon Center/Battery/Newtonsoft.Json.dll | 12.0.3.23909 | managed-PE; 0x14c; CLR 0x9 | Newtonsoft.Json 12.0.0.0 | `9000cfcb27d21ff1d9f7196b678ecc93be98af2a14254c03a554318891e6cb23` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/API_NB_Burn Recovery.dll | 1.0.2111.0401 | managed-PE; 0x14c; CLR 0x1 | API_NB_Burn Recovery 1.0.2111.401 | `60c04a3e52e5543cb5986b222dfc0920103d0dca492e10adc5b63e68df33411b` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/F3_Upgrade.exe | 1.0.2110.1301 | native-PE; 0x14c; CLR — | — | `4e87065b5c3ffa4d42ade258bac28d66628fd75598675d5fdd6235d15c9fdd35` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/Microsoft.WindowsAPICodePack.Shell.dll | 1.1.0.0 | managed-PE; 0x14c; CLR 0x1 | Microsoft.WindowsAPICodePack.Shell 1.1.0.0 | `316eda5e15278bfe228319a74de21230e0a8b5956af54b375b06608ffcaf7617` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/Microsoft.WindowsAPICodePack.dll | 1.1.2.0 | managed-PE; 0x14c; CLR 0x1 | Microsoft.WindowsAPICodePack 1.1.2.0 | `d1643df6775c870a2d8adbeb067ed8bb03d693e620e7db208612e86129fac250` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/RE_DRIVE2/LaunchDVD.exe | 1.1000.2105.501 | native-PE; 0x8664; CLR — | — | `7d0c1c983b346b2a56e3bf212fc0d93415f0d1b3547e7b660da37b20def9c4d4` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/RE_DRIVE2/ReAgentcCmd.exe | 3.1000.1608.2901 | native-PE; 0x8664; CLR — | — | `85cb8a71667d07827ac42a063ed09f5a48fc061cb0839ffe040974bca73ff0ee` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/RE_DRIVE2/msiRecovery.exe | 1.1000.2405.2601 | native-PE; 0x8664; CLR — | — | `06405f3bf0ce3d5673a6bb422a6dbb40d0f105294a195fbce16ce6bab0cad664` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/Recovery1.exe | 5.1000.2110.1201 | native-PE; 0x8664; CLR — | — | `a538565e620d8abb90e0c0f9737898c452d687aa25ae21f90b9b1752f0741a6a` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/WinRE2_Upgrade.exe | 1.0.2108.1101 | native-PE; 0x14c; CLR — | — | `d87f534dfdc30413cdc43f20d634e326c75fe2cc3c4e6f2485acb45a13242363` |
| nbfoundation/pf/MSI/One Dragon Center/Burn Recovery/msiRecovery.exe | 1.1000.2405.2601 | native-PE; 0x8664; CLR — | — | `06405f3bf0ce3d5673a6bb422a6dbb40d0f105294a195fbce16ce6bab0cad664` |
| nbfoundation/pf/MSI/One Dragon Center/DotNetZip.dll | 1.11.0 | managed-PE; 0x14c; CLR 0x9 | DotNetZip 1.11.0.0 | `b96c5090e1606a4366f07642b5d580f30d635f87f61d324ec2a53ec7e3636cb2` |
| nbfoundation/pf/MSI/One Dragon Center/GE73_Sync.exe | 1.0.0.10 | managed-PE; 0x14c; CLR 0x20003 | GE73_Sync 1.0.0.10 | `a1239e3e74db9f8edcb4dc933d6171e3a55929cec7d46b27a334beac9380e175` |
| nbfoundation/pf/MSI/One Dragon Center/GInf.dll | 2.0.1908.2701 | native-PE; 0x8664; CLR — | — | `92555c7f8f92953862ce5d6d6883193a758e1ba7085a0956012cbe5c5aa25857` |
| nbfoundation/pf/MSI/One Dragon Center/GameCenter-OC.exe | 2.0.2006.0401 | managed-PE; 0x14c; CLR 0x1 | GameCenter-OC 2.0.2006.401 | `7096d68310d46b3444c6d5a660ae57b6a5773d1161d57c99d55784e32e52f110` |
| nbfoundation/pf/MSI/One Dragon Center/General/API_NB_General.dll | 1.0.2104.1601 | managed-PE; 0x14c; CLR 0x1 | API_NB_General 1.0.2104.1601 | `37d0ec386c3e7aaad81f6b68f53b956ba24239c22476c9513ec1465ef5da20d1` |
| nbfoundation/pf/MSI/One Dragon Center/IntelOverclockingSDK.dll | 6.1.0.9999 | managed-PE; 0x14c; CLR 0x9 | IntelOverclockingSDK 6.1.0.9999 | `59c61ef28ab6bbb61f9418a8342309fab6d18cebe448edea1f8286bd0ee54b02` |
| nbfoundation/pf/MSI/One Dragon Center/Ionic.Zip.dll | 1.9.6 | managed-PE; 0x14c; CLR 0x9 | Ionic.Zip 1.9.6.0 | `f0839345c10f615856bd061125f166de160243f4959cf49c49df3b371893bdbd` |
| nbfoundation/pf/MSI/One Dragon Center/Knowledge Base/API_NB_Knowledge Base.dll | 1.0.2103.1501 | managed-PE; 0x14c; CLR 0x1 | API_NB_Knowledge Base 1.0.2103.1501 | `53115d12bbbd01adadfcea6f0b9c8121e2beb810aacb9b5981bd47a8249e24e3` |
| nbfoundation/pf/MSI/One Dragon Center/Knowledge Base/System.Runtime.WindowsRuntime.dll | 4.7.3190.0 built by: NET472REL1LAST_C | managed-PE; 0x14c; CLR 0x9 | System.Runtime.WindowsRuntime 4.0.0.0 | `b7a891a753f9d60da62070b81661145dd3696a0f8e83996d3f24cc85a668c069` |
| nbfoundation/pf/MSI/One Dragon Center/MSIWMIACPI2.dll | 1.0.2410.0101 | managed-PE; 0x14c; CLR 0x1 | MSIWMIACPI2 1.0.2410.101 | `1930038a503f8a6cc5824fa479e44c9fc9cf95e0dc60e78caad4b3df76533828` |
| nbfoundation/pf/MSI/One Dragon Center/Microsoft.WindowsAPICodePack.Shell.dll | 1.1.0.0 | managed-PE; 0x14c; CLR 0x1 | Microsoft.WindowsAPICodePack.Shell 1.1.0.0 | `d5e7be97513c04a7f2a71ae4c35a565b277bbc18ab7b3a785633bc0c9b396502` |
| nbfoundation/pf/MSI/One Dragon Center/Microsoft.WindowsAPICodePack.dll | 1.1.2.0 | managed-PE; 0x14c; CLR 0x1 | Microsoft.WindowsAPICodePack 1.1.2.0 | `23207a3eab6b54d528a880370816cfc739db4e8d55100ba96929ea9b8e976a5a` |
| nbfoundation/pf/MSI/One Dragon Center/MicrosoftAPP/API_NB_MicrosoftAPP.dll | 1.0.2103.1501 | managed-PE; 0x14c; CLR 0x1 | API_NB_MicrosoftAPP 1.0.2103.1501 | `f9dda6ebcb87a697c8c83098abe88092186b37ffde10756b20964c12b08ed703` |
| nbfoundation/pf/MSI/One Dragon Center/MsiHid.dll | 1.0.0.11 | native-PE; 0x14c; CLR — | — | `4f11223b08b760b31104d671aeff5e9cba8c6dc8fa89d97c03e72266629483e1` |
| nbfoundation/pf/MSI/One Dragon Center/NahimicAPI.dll | 1.1.7.0 | native-PE; 0x8664; CLR — | — | `7434e6e74c537e097b93bc5291bdf89905fed240acbe6ecf969f15e7c8e112ce` |
| nbfoundation/pf/MSI/One Dragon Center/NamedPipeClientLib.dll | 1.1.2408.0501 | managed-PE; 0x14c; CLR 0x1 | NamedPipeClientLib 1.1.2408.501 | `3bef0ce4026ef03bbfb491ceb550ce85a1ddf0451053c048a2c83a589ad8c38d` |
| nbfoundation/pf/MSI/One Dragon Center/Newtonsoft.Json.dll | 12.0.3.23909 | managed-PE; 0x14c; CLR 0x9 | Newtonsoft.Json 12.0.0.0 | `9000cfcb27d21ff1d9f7196b678ecc93be98af2a14254c03a554318891e6cb23` |
| nbfoundation/pf/MSI/One Dragon Center/ProfileHelperModel.dll | 6.1.0.9999 | managed-PE; 0x14c; CLR 0x9 | — | `1927cc43033028b2f43b4445f1c0d1432ba5efbdb4941511c300023f1093a09b` |
| nbfoundation/pf/MSI/One Dragon Center/ServiceSwitch/ServiceSwitch.exe | 1.0.1812.1201 | managed-PE; 0x14c; CLR 0x20003 | ServiceSwitch 1.0.1812.1201 | `6c1f85b35f3fa7547c13067e42a59f8098a810b86cfe35da8f08dd742346a943` |
| nbfoundation/pf/MSI/One Dragon Center/SevenZipSharp.dll | 1.0.19.0 | managed-PE; 0x14c; CLR 0x9 | SevenZipSharp 1.0.19.0 | `440faa5b5d968e8293cf32bdbcd01d98287ee5191de860d1b32377b70f22e591` |
| nbfoundation/pf/MSI/One Dragon Center/Support/API_LiveUpdate.dll | 2.0.0.55 | native-PE; 0x14c; CLR — | — | `fe61786051906f8e2c409da399c7fea4239bf49a06a7d3d27f39a08cfe2c1508` |
| nbfoundation/pf/MSI/One Dragon Center/Support/unrar.dll | 5.50.0 | native-PE; 0x14c; CLR — | — | `799232852e8813bcbf846e3d78abfbddf62eb59a639f0a74350a738204e5ab91` |
| nbfoundation/pf/MSI/One Dragon Center/System Info/API_NB_System Info.dll | 1.0.2106.3001 | managed-PE; 0x14c; CLR 0x1 | API_NB_System Info 1.0.2106.3001 | `383ee53a504b53c43c37123395438edf37f1390a6f99fd49f02c6fda0295b18c` |
| nbfoundation/pf/MSI/One Dragon Center/System Info/System.Runtime.WindowsRuntime.dll | 4.7.3190.0 built by: NET472REL1LAST_C | managed-PE; 0x14c; CLR 0x9 | System.Runtime.WindowsRuntime 4.0.0.0 | `b7a891a753f9d60da62070b81661145dd3696a0f8e83996d3f24cc85a668c069` |
| nbfoundation/pf/MSI/One Dragon Center/System Info/Windows.winmd | 未声明 | managed-PE; 0x14c; CLR 0x1 | Windows 255.255.255.255 | `aa303aa0db429bd98ce45c5e074ad7d0df6b3b771df3b97eba962dee0c0df470` |
| nbfoundation/pf/MSI/One Dragon Center/System Monitor/API_NB_System Monitor.dll | 1.0.2311.2001 | managed-PE; 0x14c; CLR 0x1 | API_NB_System Monitor 1.0.2311.2001 | `0f59373000d05f48016acbb712a8e31a79e1010b9b046259ab7b367adf2a653d` |
| nbfoundation/pf/MSI/One Dragon Center/System Monitor/NamedPipeClientLib.dll | 1.0.1904.1701 | managed-PE; 0x14c; CLR 0x1 | NamedPipeClientLib 1.0.1904.1701 | `dfcd00776ba6a510b765173a533f2e14202d08c6dee864269ee3aa88f932aa83` |
| nbfoundation/pf/MSI/One Dragon Center/System Monitor/Newtonsoft.Json.dll | 6.0.8.18111 | managed-PE; 0x14c; CLR 0x9 | Newtonsoft.Json 6.0.0.0 | `78bedd9fce877a71a8d8ff9a813662d8248361e46705c4ef7afc61d440ff2eeb` |
| nbfoundation/pf/MSI/One Dragon Center/System Restore/API_NB_System Restore.dll | 1.0.2106.0201 | managed-PE; 0x14c; CLR 0x1 | API_NB_System Restore 1.0.2106.201 | `46ad8cc42929b096bf7c231071cfb4116bf8e34dcb7d054ba67442d06d22d663` |
| nbfoundation/pf/MSI/One Dragon Center/True Color/API_NB_True Color.dll | 1.0.2104.1601 | managed-PE; 0x14c; CLR 0x1 | API_NB_True Color 1.0.2104.1601 | `b361e1aa5d197307dff77f2ae275639ea3516913eb74e93fcad0345ad26cef44` |
| nbfoundation/pf/MSI/One Dragon Center/Update/API_NB_Update.dll | 1.0.2407.2901 | managed-PE; 0x14c; CLR 0x1 | API_NB_Update 1.0.2407.2901 | `a72c20d9aed1aaaac5852bb462cd3f75d452d90e28a07f46b7e29a6b6bdf91a6` |
| nbfoundation/pf/MSI/One Dragon Center/Update/DotNetZip.dll | 1.11.0 | managed-PE; 0x14c; CLR 0x9 | DotNetZip 1.11.0.0 | `c085d0b7032a7d36309847a6c6a689cd80ec6dea518a434ebe0c2019712e5663` |
| nbfoundation/pf/MSI/One Dragon Center/Update/Newtonsoft.Json.dll | 12.0.3.23909 | managed-PE; 0x14c; CLR 0x9 | Newtonsoft.Json 12.0.0.0 | `9000cfcb27d21ff1d9f7196b678ecc93be98af2a14254c03a554318891e6cb23` |
| nbfoundation/pf/MSI/One Dragon Center/devcon.exe | 6.1.7600.16385 (win7_rtm.090713-1255) | native-PE; 0x8664; CLR — | — | `0aaefd9f967dd9e92889fa04ccf446b69cc9b5daf593cfe662ee859e7cdbf05a` |
| nbfoundation/pf/MSI/One Dragon Center/msiapcfg.dll | 10.0.10011.16384 | native-PE; 0x14c; CLR — | — | `52a960f14f489dac581c988415402dd03f9b9c919332749a47973b2de64cd7be` |
| nbfoundation/syswow64/MSIService.exe | 1, 0, 10, 0 | native-PE; 0x14c; CLR — | — | `38b5536abcf7eaa9f249b6c325e29bdcc450e7c51239785466d6afc236fe9a90` |
| nbfoundation/syswow64/MSIWmiAcpi.dll | 1.0.11.0 | native-PE; 0x14c; CLR — | — | `3a2b4ebe2b3f6e6baaa7bd472237da778c5869c9feef82b450aecbe6ebccb3bd` |
| nbfoundation/syswow64/msiapcfg.dll | 10.0.10011.16384 | native-PE; 0x14c; CLR — | — | `d02e23490f28d7636b13d8b2a8a830c00afdc2ba8747cece9ad9a6dd95fe301f` |
| nbfoundation/win/RE_DRIVE/RECOVERY/BurnRecovery.exe | 5.0.2107.1401 | native-PE; 0x14c; CLR — | — | `31a0cf3ebe05c0262f18c51dcabdc8a04984a8c29fd22f79e8d8b372c38b3da8` |
