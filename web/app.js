'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const put = (id, value) => { $(id).textContent = value; };
  const isFile = location.protocol==='file:';
  $('file-online-notice').hidden=!isFile;
  const storage = {
    get(key, fallback='') { try { return localStorage.getItem(`tb-${key}`) || fallback; } catch { return fallback; } },
    set(key,value) { try { localStorage.setItem(`tb-${key}`,value); } catch { /* File URLs and privacy modes can deny storage. */ } }
  };
  const game = new TBRacing.Game($('race'), (status,stats) => {put('race-status',status);put('race-stats',stats);}, result => put('results',result));
  function options(select, tracks){
    select.replaceChildren();
    for(const track of tracks){const option=document.createElement('option');option.value=track.id;option.textContent=track.name;select.append(option);}
  }
  options($('track'),TBRacing.tracks);
  $('quality').value=storage.get('quality','high');
  if(!['ultra','high','low'].includes($('quality').value))$('quality').value='high';
  game.quality=$('quality').value;
  $('quality').addEventListener('change',()=>{game.quality=$('quality').value;storage.set('quality',game.quality);game.resize();});
  $('track').addEventListener('change',()=>game.preview($('track').value));
  function practice(){
    if(room){put('network-status','Leave your online room before starting practice.');return;}
    put('race-mode','OFFLINE PRACTICE');put('results','');game.start($('track').value);
  }
  $('start').addEventListener('click',practice);
  $('fullscreen').addEventListener('click',async()=>{
    try{if(document.fullscreenElement)await document.exitFullscreen();else if($('race-stage').requestFullscreen)await $('race-stage').requestFullscreen();else put('results','Fullscreen is unavailable in this browser.');$('race').focus({preventScroll:true});}
    catch{put('results','Fullscreen was blocked. You can still race in this window.');}
  });
  $('driver-name').value=storage.get('name','Driver');
  $('server-url').value=storage.get('server');
  let socket=null, welcomed=false, playerId=null, serverTracks=[], room=null, rooms=[], retryTimer=null, attempts=0, intentional=false, focusedMatch=null;
  function validURL(value){
    const url=new URL(value.trim());
    if(!['ws:','wss:'].includes(url.protocol)||url.username||url.password||url.hash||!url.hostname)throw new Error('Use a ws:// or wss:// address without credentials or a fragment.');
    if(location.protocol==='https:'&&url.protocol!=='wss:')throw new Error('This HTTPS page requires a secure wss:// server.');
    return url.href;
  }
  const supplied=new URLSearchParams(location.search).get('server');
  if(supplied){try{$('server-url').value=validURL(supplied);}catch{put('network-status','The server parameter was rejected. Enter a valid WebSocket address.');}}
  function controls(){
    const pending=!!socket&&!welcomed;
    $('connect').disabled=isFile||!!socket||!!retryTimer;
    $('disconnect').disabled=!socket&&!retryTimer;
    $('create').disabled=!welcomed||!!room||!serverTracks.length;
    $('ready').disabled=!welcomed||!room||room.status!=='waiting';
    $('leave').disabled=!welcomed||!room;
    $('start').disabled=!!room;
    $('server-url').disabled=isFile||!!socket||!!retryTimer;
    put('connection-badge',welcomed?'CONNECTED':pending?'CONNECTING':retryTimer?'RECONNECTING':'NOT CONNECTED');
  }
  function send(message){
    if(!welcomed||socket?.readyState!==WebSocket.OPEN)return false;
    socket.send(JSON.stringify(message));return true;
  }
  function node(tag,text,className){
    const el=document.createElement(tag);el.textContent=text;if(className)el.className=className;return el;
  }
  function championshipTrophy(){
    const trophy=document.createElementNS('http://www.w3.org/2000/svg','svg');
    trophy.setAttribute('viewBox','0 0 64 64');trophy.setAttribute('class','champion-trophy');trophy.setAttribute('role','img');trophy.setAttribute('aria-label','Championship trophy');
    const cup=document.createElementNS('http://www.w3.org/2000/svg','path');
    cup.setAttribute('d','M18 8h28v10c0 11-5.6 18.9-12 21v8h9v7H21v-7h9v-8c-6.4-2.1-12-10-12-21V8zm-7 5H5v6c0 8 5 13 14 14l-2-6c-5-1-7-4-7-8zm40 0v6c0 4-2 7-7 8l-2 6c9-1 14-6 14-14v-6z');
    cup.setAttribute('fill','currentColor');trophy.append(cup);return trophy;
  }
  function renderRooms(){
    $('rooms').replaceChildren();
    if(!rooms.length){$('rooms').append(node('p',welcomed?'No open rooms. Create the first grid.':'Connect to see available rooms.','quiet'));return;}
    for(const entry of rooms){
      const row=node('div','','room-row'),label=node('div',entry.name);
      label.append(node('small',`${entry.mode==='tournament'?'Knockout cup':'Head-to-head'} · ${entry.count}/${entry.capacity} · ${entry.status}`));
      const join=node('button','Join');join.disabled=!welcomed||!!room||entry.status!=='waiting'||entry.count>=entry.capacity;
      join.addEventListener('click',()=>send({type:'join',roomId:entry.id,name:$('driver-name').value.trim()||'Driver'}));
      row.append(label,join);$('rooms').append(row);
    }
  }
  function clearRoom(){
    room=null;focusedMatch=null;game.leaveOnline();put('race-mode','OFFLINE PRACTICE');put('room-detail','No room joined.');$('bracket').replaceChildren();put('ready','Ready to race');renderRooms();controls();
  }
  function renderRoom(){
    const detail=$('room-detail');detail.replaceChildren();
    detail.append(node('strong',`${room.name} · ${room.status}`));
    for(const p of room.players){
      const row=node('div','','player-row');row.append(node('span',`${p.name}${p.id===playerId?' (you)':''}`),node('span',!p.connected?'Disconnected':p.ready?'Ready':'Not ready'));detail.append(row);
    }
    const me=room.players.find(p=>p.id===playerId);put('ready',me?.ready?'Cancel ready':'Ready to race');
    const bracket=$('bracket');bracket.replaceChildren();
    if(room.bracket?.length){
      bracket.append(node('h3','Tournament bracket'));
      const name=id=>room.players.find(p=>p.id===id)?.name||(id?'Driver':'To be decided');
      for(const match of room.bracket)bracket.append(node('div',`Round ${match.round} · ${name(match.player1Id)} vs ${name(match.player2Id)} · ${match.winnerId?`${name(match.winnerId)} wins`:match.status}`,'bracket-match'));
    }
    if(room.championId){
      const champion=node('div','','tournament-champion'),copy=node('div');
      copy.append(node('span','TOURNAMENT CHAMPION','champion-label'),node('strong',room.players.find(p=>p.id===room.championId)?.name||'Driver'));
      champion.append(championshipTrophy(),copy);bracket.append(champion);
    }
    controls();renderRooms();
  }
  function trackValid(t){
    return t&&typeof t.id==='string'&&typeof t.name==='string'&&Array.isArray(t.points)&&t.points.length>=3&&t.points.length<=2048&&t.points.every(p=>Number.isFinite(p.x)&&Number.isFinite(p.y)&&Math.abs(p.x)<1e7&&Math.abs(p.y)<1e7)&&Number.isFinite(t.roadWidth)&&t.roadWidth>0&&Number.isFinite(t.laps)&&Array.isArray(t.features)&&t.features.length<=1024&&t.features.every(f=>['zipper','oil','puddle'].includes(f.kind)&&Number.isFinite(f.x)&&Number.isFinite(f.y)&&Number.isFinite(f.radius));
  }
  function receive(raw){
    if(typeof raw!=='string'||raw.length>1000000)return;
    let data;try{data=JSON.parse(raw);}catch{return;}
    if(!data||typeof data.type!=='string')return;
    if(data.type==='welcome'){
      if(typeof data.playerId!=='string'||!Array.isArray(data.tracks)||!data.tracks.length||data.tracks.length>100||!data.tracks.every(trackValid)){put('network-status','Server returned an incompatible track list.');socket?.close(1000,'Incompatible protocol');return;}
      welcomed=true;attempts=0;playerId=data.playerId;serverTracks=data.tracks;options($('online-track'),serverTracks);put('network-status','Connected. Join a room or create your own; everyone must be ready to start.');controls();renderRooms();
      return;
    }
    if(!welcomed)return;
    switch(data.type){
      case 'lobby':
        if(!Array.isArray(data.rooms))return;
        rooms=data.rooms.slice(0,100).filter(r=>r&&typeof r.id==='string'&&typeof r.name==='string');renderRooms();break;
      case 'room':
        if(typeof data.id!=='string'||!Array.isArray(data.players)||data.players.length>8||!data.players.every(p=>p&&typeof p.id==='string'&&typeof p.name==='string'))return;
        if(data.bracket&&(!Array.isArray(data.bracket)||data.bracket.length>7))return;
        if(!room||room.id!==data.id){game.online(playerId,serverTracks);put('results','');put('race-mode','SERVER MULTIPLAYER');}
        room=data;renderRoom();break;
      case 'snapshot':
        if(!room||data.roomId!==room.id||typeof data.matchId!=='string'||!['countdown','racing','finished'].includes(data.state)||!Array.isArray(data.cars)||data.cars.length>8||!data.cars.every(c=>c&&typeof c.id==='string'&&typeof c.name==='string'&&[c.x,c.y,c.heading,c.speed,c.lap].every(Number.isFinite))||!serverTracks.some(t=>t.id===data.trackId))return;
        game.snapshot(data);
        if(data.state!=='finished'&&focusedMatch!==data.matchId&&data.cars.some(c=>c.id===playerId)){
          focusedMatch=data.matchId;$('practice').scrollIntoView({block:'start'});$('race').focus({preventScroll:true});
        }
        break;
      case 'left': clearRoom();put('network-status','You left the room. Practice is available again.');break;
      case 'error': if(typeof data.message==='string')put('network-status',data.message.slice(0,500));break;
    }
  }
  function open(url){
    intentional=false;welcomed=false;rooms=[];renderRooms();
    let current;try{current=new WebSocket(url);socket=current;}catch{put('network-status','Unable to open that WebSocket address.');socket=null;controls();return;}
    controls();put('network-status','Connecting… Waiting for the server welcome.');
    const timeout=setTimeout(()=>{if(socket===current&&!welcomed)current.close(1000,'Welcome timeout');},10000);
    current.addEventListener('message',e=>{if(socket===current)receive(e.data);});
    current.addEventListener('error',()=>{if(socket===current)put('network-status','Connection failed. Check the server address, TLS certificate, and allowed origin.');});
    current.addEventListener('close',()=>{
      clearTimeout(timeout);if(socket!==current)return;socket=null;welcomed=false;playerId=null;rooms=[];clearRoom();
      if(!intentional&&attempts<5){
        const wait=Math.min(1000*2**attempts++,15000);
        put('network-status',`Connection lost. Reconnecting in ${wait/1000}s. A new connection requires joining the room again.`);
        retryTimer=setTimeout(()=>{retryTimer=null;open(url);},wait);
      }else put('network-status',intentional?'Disconnected. Offline practice is available.':'Could not reconnect. Check your host, then connect again.');
      controls();
    });
  }
  $('connect-form').addEventListener('submit',e=>{
    e.preventDefault();
    if(isFile){put('network-status','Multiplayer requires the hosted racing lounge or an explicitly allowed HTTP origin. Offline ZIP practice is available.');return;}
    if(socket||retryTimer)return;
    try{const url=validURL($('server-url').value);storage.set('server',url);storage.set('name',$('driver-name').value.trim()||'Driver');attempts=0;open(url);}catch(error){put('network-status',error.message);}
  });
  $('disconnect').addEventListener('click',()=>{
    intentional=true;clearTimeout(retryTimer);retryTimer=null;if(socket)socket.close(1000,'User disconnect');else{welcomed=false;clearRoom();put('network-status','Disconnected.');}controls();
  });
  $('create-form').addEventListener('submit',e=>{e.preventDefault();send({type:'create',name:$('driver-name').value.trim()||'Driver',mode:$('mode').value,trackId:$('online-track').value});});
  $('ready').addEventListener('click',()=>{const me=room?.players.find(p=>p.id===playerId);if(me)send({type:'ready',ready:!me.ready});});
  $('leave').addEventListener('click',()=>send({type:'leave'}));
  setInterval(()=>{if(!document.hidden&&game.drivingOnline())send({type:'input',...game.input()});},1000/30);
  setInterval(()=>{if(welcomed&&!game.drivingOnline())send({type:'input',accelerate:false,brake:false,steer:0,fire:false});},20000);
  window.addEventListener('blur',()=>{if(welcomed)send({type:'input',accelerate:false,brake:true,steer:0,fire:false});});
  document.addEventListener('visibilitychange',()=>{if(document.hidden&&welcomed)send({type:'input',accelerate:false,brake:true,steer:0,fire:false});});
  controls();
  if(isFile)put('network-status','Offline ZIP mode · practice enabled · multiplayer requires the hosted site.');
  if('serviceWorker' in navigator&&(location.protocol==='https:'||location.hostname==='localhost'||location.hostname==='127.0.0.1')){
    navigator.serviceWorker.register('./sw.js',{scope:'./'}).catch(()=>{/* Extracted ZIP practice never depends on the service worker. */});
  }
})();
