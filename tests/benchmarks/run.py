"""Run only in a disposable --network none container with NET_ADMIN/NET_RAW.

Both veth endpoints belong to this container's private network namespace.
"""
from pathlib import Path
import json
import os
import platform
import re
import signal
import subprocess
import sys
import time

binary, capture, directory = map(Path, sys.argv[1:4])
directory.mkdir(parents=True, exist_ok=True)
subprocess.run(['ip', 'link', 'add', 'peek-tx', 'type', 'veth', 'peer', 'name', 'peek-rx'], check=True)
for device in ['peek-tx', 'peek-rx']:
    subprocess.run(['ip', 'link', 'set', device, 'up'], check=True)
records = []
try:
    for repeat in range(1, 4):
        for mode in ['kernel', 'userspace']:
            log = directory / f'{mode}-{repeat}.log'
            command = [str(binary.resolve()), '--interface', 'peek-rx', '--filter', 'tcp port 80', '--idle-seconds', '30']
            if mode == 'userspace': command += ['--userspace-filter']
            with log.open('wb') as error:
                engine = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=error)
                try:
                    time.sleep(0.5)
                    if engine.poll() is not None: raise RuntimeError(log.read_text())
                    replay_command = ['tcpreplay', '--intf1=peek-tx', '--topspeed', '--loop=10', str(capture)]
                    replay = subprocess.run(replay_command, capture_output=True, text=True, check=True)
                    (directory / f'replay-{mode}-{repeat}.log').write_text(replay.stdout + replay.stderr)
                    # Allow pending packets to drain before graceful closure.
                    time.sleep(2)
                    engine.send_signal(signal.SIGINT)
                    if engine.wait(timeout=30) != 0: raise RuntimeError(log.read_text())
                finally:
                    if engine.poll() is None: engine.kill(); engine.wait()
            text = log.read_text()
            values = {key: float(value) if '.' in value else int(value)
                      for key, value in re.findall(r'(\w+)=([0-9.]+)', text)}
            values.update(mode=mode, repeat=repeat,
                          parsed_packets_per_second=round(values['packets'] / values['read_span_seconds'], 2),
                          engine_command=command, replay_command=replay_command)
            records.append(values)
            print(json.dumps(values), flush=True)
finally:
    subprocess.run(['ip', 'link', 'delete', 'peek-tx'], check=True)
(directory / 'results.json').write_text(json.dumps({'kernel': platform.release(), 'platform': platform.platform(),
    'visible_cpus': os.cpu_count(), 'cpuinfo': Path('/proc/cpuinfo').read_text().split('model name')[1].splitlines()[0],
    'runs': records}, indent=2))
