# Flexible machining scene migration

The `FactoryMachiningTwin` visual design was translated from the local
`robot_simulation/web_workbench/static/scene3d.js` reference supplied by the
project owner. The source is a Three.js procedural scene, not a Unity project.

Migrated visual rules:

- light grid workshop and near-vertical soft lighting;
- enclosed CNC cells with blue sliding safety doors;
- orange articulated robots on teal rail beds;
- a 60-slot line-side ASRS, stacker crane, transfer rollers and human stations.

The original two-track/seven-machine simulation topology was intentionally not
copied. Unity keeps this project's three-line MQTT/SimPy topology so every
visible device remains connected to the existing live state and command model.

The supplied source README identifies that project as MIT licensed; no separate
repository-level `LICENSE` file was present in the supplied folder.
