
**Toggle Power Plan**

A lightweight Windows batch script
that switches between **Balanced** and **High Performance** power modes.

No GUI.
No external tools.
Uses native Windows `powercfg`.


**What It Does**

When executed, the script:

1. Detects the currently active power plan
2. Compares its GUID
3. Switches to the opposite plan
4. Displays the new active scheme

Run once → High Performance
Run again → Balanced


**How It Works**

The script calls:

```
powercfg /getactivescheme
```

It extracts the active power plan GUID
and compares it against default Windows GUIDs:

| Plan             | GUID                                 |
| ---------------- | ------------------------------------ |
| Balanced         | 381b4222-f694-41f0-9685-ff5bb260df2e |
| High Performance | 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c |

If the current plan is Balanced → it activates High Performance.
Otherwise → it switches back to Balanced.

All logic is contained in a single `.bat` file.


**Use Cases**

* Quick performance boost before gaming or rendering
* Switching back to power-saving mode afterward
* Binding to a keyboard shortcut
* Desktop one-click performance toggle


**Requirements**

* Windows
* Default power plan GUIDs available

If custom power plans are used, GUIDs may differ.

To check available plans:

```
powercfg /list
```


**Platform**

Windows


Part of **X-LAB** — practical automation utilities.
