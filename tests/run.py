#!/usr/bin/env python3
"""Compila, migra una base exclusiva *_tests, arranca la API y ejecuta pruebas HTTP."""
from pathlib import Path
import os, shutil, socket, subprocess, sys, time, urllib.request, tempfile, uuid
root=Path(__file__).resolve().parents[1]
settings={}
config=Path(os.environ.get('BIBLIOTECA_ENV_FILE',str(root/'.env')))
if not config.is_file():config=Path.home()/'Documents/Codex/.secrets/biblioteca-virtual.env'
for line in config.read_text().splitlines():
 if line and not line.startswith('#'):
  k,v=line.split('=',1);settings[k]=v
name=settings['DB_NAME']+'_'+uuid.uuid4().hex[:8]+'_tests'
assert name.endswith('_tests') and all(c.isalnum() or c=='_' for c in name)
dotnet=os.environ.get('DOTNET_BIN') or shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
keys_dir=tempfile.mkdtemp(prefix='biblioteca-test-keys-')
test_email='admin-'+uuid.uuid4().hex[:8]+'@example.test'
test_password='BibliotecaTest123!'
env={**os.environ,'Bootstrap__AdminEmail':test_email,'Bootstrap__AdminPassword':test_password,'TEST_ADMIN_EMAIL':test_email,'TEST_ADMIN_PASSWORD':test_password,'ASPNETCORE_ENVIRONMENT':'Development','Demo__EnableFailureSimulation':'true','ASPNETCORE_URLS':'http://localhost:5181','DataProtection__KeysPath':keys_dir,'ConnectionStrings__Library':f"Server={settings['DB_HOST']};Port={settings['DB_PORT']};Database={name};User={settings['DB_USER']};Password={settings['DB_PASSWORD']};SslMode=Preferred;"}
with socket.socket() as sock:
 if sock.connect_ex(('127.0.0.1',5181))==0:raise SystemExit('Puerto 5181 ocupado: no se ejecutan pruebas contra un servidor desconocido.')
subprocess.run([dotnet,'build',str(root/'BibliotecaVirtual.slnx'),'-c','Release','-m:1','-nodeReuse:false'],env=env,check=True)
dll=root/'src/Biblioteca.Api/bin/Release/net10.0/Biblioteca.Api.dll'
subprocess.run([dotnet,str(dll),'--migrate'],cwd=dll.parent,env=env,check=True)
subprocess.run([dotnet,str(dll),'--create-admin'],cwd=dll.parent,env=env,check=True)
(root/'work').mkdir(exist_ok=True)
with (root/'work/integration-api.log').open('w') as log:
 proc=subprocess.Popen([dotnet,str(dll)],cwd=dll.parent,env=env,stdout=log,stderr=subprocess.STDOUT)
 try:
  for _ in range(100):
   try:
    with urllib.request.urlopen('http://localhost:5181/health/ready',timeout=1) as r:
     if r.status==200:break
   except Exception:
    if proc.poll() is not None:raise RuntimeError('La API finalizó; revisa work/integration-api.log')
    time.sleep(.2)
  else:raise RuntimeError('La API no respondió; revisa work/integration-api.log')
  subprocess.run([sys.executable,str(root/'tests/integration.py')],env=env,check=True)
 finally:
  proc.terminate()
  try:proc.wait(timeout=10)
  except subprocess.TimeoutExpired:proc.kill();proc.wait()
  shutil.rmtree(keys_dir)
