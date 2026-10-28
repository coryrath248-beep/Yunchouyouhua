# SUPCON Factory Digital Twin

这是一个 Unity 2022.3 桌面端数字孪生前端。它不重新实现调度逻辑：订单、工艺、故障、KPI、AGV 指令合法性仍由
`25-AdventureX-SUPCON-Hackathon` 中的 Python/SimPy 后端负责。

## 当前最小闭环

```text
demo_dispatch_agent.py -> MQTT command -> Python/SimPy
Python/SimPy -> MQTT status -> Unity AGV/设备动画与 KPI 面板
```

- Unity 使用 Robotics-Warehouse 的货架模型，场景和前端代码可以直接修改。
- `Warehouse-AGV-Deep-RL` 仅作为分层控制参考；它需要 Unity 6/ML-Agents 4.0，因此没有硬接入当前 Unity 2022.3 工程。
- Unity 是只读可视化客户端，不负责判断动作是否合法。

## 第一次生成场景

用 Unity Hub 打开本目录，然后选择菜单 `Factory Twin > Build Demo Scene`。也可以使用批处理：

```powershell
& 'D:\软件\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe' `
  -batchmode -quit -projectPath "$PWD" `
  -executeMethod FactoryTwinEditor.FactorySceneBuilder.Build `
  -logFile "$PWD\Logs\factory-twin-build.log"
```

生成的场景是 `Assets/Scenes/FactoryDigitalTwin.unity`。

## 启动本地闭环

已完成一次构建后，最简单的方式是在项目根目录运行：

```powershell
.\Start-FactoryTwin.ps1
```

它会在后台启动 Broker、SimPy 和演示 Agent，并打开可交互的 Unity 窗口。结束时运行：

```powershell
.\Stop-FactoryTwin.ps1
```

也可以手动启动。进入 Python 项目目录，打开三个终端，统一使用以下环境变量：

```powershell
$env:TOPIC_ROOT='NLDF_LOCAL'
$env:MQTT_BROKER_HOST='127.0.0.1'
$env:MQTT_BROKER_PORT='1883'
$env:PYTHONIOENCODING='utf-8'
$env:PYTHONUTF8='1'
```

依次运行：

```powershell
uv sync --python 3.11
uv run python tools/local_broker.py
uv run python run_multi_line_simulation.py --no-fault
uv run python tools/demo_dispatch_agent.py
```

最后在 Unity 中打开生成场景并按 Play。右键拖动旋转，滚轮缩放，WASD 平移。

## 协议

Unity 订阅：

- `NLDF_LOCAL/+/agv/+/status`
- `NLDF_LOCAL/+/station/+/status`
- `NLDF_LOCAL/+/conveyor/+/status`
- `NLDF_LOCAL/warehouse/+/status`
- `NLDF_LOCAL/kpi/status`
- `NLDF_LOCAL/+/alerts`
- `NLDF_LOCAL/response/+`

AGV 状态新增了可选字段 `target_position`，同时移动开始消息会携带正确的 `estimated_time`。旧客户端忽略新增字段即可。

## WebGL 说明

当前实现使用桌面端 MQTT/TCP。浏览器中的 Unity WebGL 不能直接打开原生 TCP Socket；要发布 WebGL，需要把传输层换成
MQTT over WebSocket，并在 Broker 开启 WebSocket 监听。仿真协议和场景层无需改变。
