# 本机 System Monitor 温度读取证据

本机 `API_NB_System Monitor.dll`，文件版本 1.0.2201.1201，SHA-256 `51a7220291eea029a905131198bb428a4692af690b25065378b2e4d195a929d4`，MVID `83f51b4f-909b-4000-800e-5e3d523c5546`。REA Evidence `ev_fa9a6b52e0406a979efc6b5380ff8806f54c336709e0111ace671a64eaf6cf29` 静态元数据检查覆盖 complete，无 issues；[完整记录](rea-installed-system-monitor.json)。分析未加载或执行原厂程序集。

离线官方包同名文件 SHA-256 为 `0f59373000d05f48016acbb712a8e31a79e1010b9b046259ab7b367adf2a653d`，MVID 不同。因此这里只使用从本机复制并校验的 artifact 作为实现依据。ILSpy 对本机 artifact 只作离线反编译，输出文本 SHA-256 为 ca8112a0f63dcb1a14501bcd9b15b1afbbab2201ca1d4bd1c440bc0cf2f55803；行号只作为辅助定位。元数据 token 如未另行核对则不作 ABI 结论。

该版本在构造处第 5088/5089 行创建 `SELECT CPU FROM MSI_CPU`、`SELECT VGA FROM MSI_VGA` 查询；两个 getter 均取 ordinal 1，0/255 返回 N/A，其他值传入明确命名的 Celsius 转换。此前只检查 Base Module 的结论不覆盖此独立模块。项目不调用原厂类的构造、UpdateAll、服务管道或驱动，只复现已确认的 WMI 属性查询。

### GetCpuTemperature

来源：反编译文本第 6178 行。

```csharp
public string GetCpuTemperature(bool inCelsiusDegree)
		{
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Expected Obj, but got Unknown
			nItem = 0;
			try
			{
				nItem = 0;
				ManagementObjectEnumerator enumerator = Searcher_MSI_CPU.Get().GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						ManagementObject val = (ManagementObject)enumerator.Current;
						if (nItem == 1)
						{
							int num = Convert.ToInt16(((ManagementBaseObject)val)["CPU"].ToString());
							if (num == 255 || num == 0)
							{
								CpuTemperature_int = 0;
								return "N/A";
							}
							CpuTemperature_int = num;
							return ConvertToCurrentTemperatureUnit(num, inCelsiusDegree);
						}
						nItem++;
					}
				}
				finally
				{
					((IDisposable)enumerator)?.Dispose();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("GetCpuTemperature: " + ex.Message);
			}
			CpuTemperature_int = 0;
			return "N/A";
		}
```

### GetGpuTemperature

来源：反编译文本第 6132 行。

```csharp
public string GetGpuTemperature(bool inCelsiusDegree)
		{
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Expected Obj, but got Unknown
			nItem = 0;
			try
			{
				nItem = 0;
				ManagementObjectEnumerator enumerator = Searcher_MSI_VGA.Get().GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						ManagementObject val = (ManagementObject)enumerator.Current;
						if (nItem == 1)
						{
							int num = Convert.ToInt16(((ManagementBaseObject)val)["VGA"].ToString());
							switch (num)
							{
							case 0:
								GpuTemperature_int = 0;
								return "N/A";
							case 255:
								GpuTemperature_int = 0;
								return "N/A";
							default:
								GpuTemperature_int = num;
								return ConvertToCurrentTemperatureUnit(num, inCelsiusDegree);
							}
						}
						nItem++;
					}
				}
				finally
				{
					((IDisposable)enumerator)?.Dispose();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("GetGpuTemperature: " + ex.Message);
			}
			GpuTemperature_int = 0;
			return "N/A";
		}
```

### ConvertToCurrentTemperatureUnit

来源：反编译文本第 6123 行。

```csharp
private string ConvertToCurrentTemperatureUnit(int temperatureInCelsius, bool inCelsiusDegree)
		{
			if (inCelsiusDegree)
			{
				return temperatureInCelsius + " ℃";
			}
			return (double)temperatureInCelsius / 5.0 * 9.0 + 32.0 + " ℉";
		}
```
