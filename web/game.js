/* Classic scripts deliberately support extracted ZIPs opened with file://. */
'use strict';
window.TBRacing = (() => {
  const TAU = Math.PI * 2;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const angle = v => Math.atan2(Math.sin(v), Math.cos(v));
  const colors = ['#ed404b', '#e5ba6b', '#7cadce', '#b39dd7', '#6cc8a3', '#ed9e65', '#f092bf', '#b9c568'];
  const definitions = [
    ['sunshine-speedway', 'Sunshine Speedway', [[100,100],[500,80],[900,100],[1000,300],[900,500],[500,520],[100,500],[0,300]]],
    ['desert-dunes', 'Desert Dunes', [[100,100],[700,100],[900,200],[900,450],[700,500],[550,400],[400,450],[300,650],[100,650],[0,400]]],
    ['pine-ridge', 'Pine Ridge Raceway', [[0,0],[400,-50],[700,50],[900,0],[1100,150],[1000,400],[750,400],[600,550],[750,700],[600,850],[250,850],[100,650],[200,450],[0,300]]],
    ['harbor-hairpins', 'Harbor Hairpins', [[0,0],[900,0],[1000,120],[900,240],[300,240],[200,360],[300,480],[900,480],[1000,600],[900,720],[0,720],[-100,360]]],
    ['midnight-mile', 'Midnight Mile', [[0,0],[600,0],[800,150],[700,350],[500,300],[350,400],[500,550],[800,550],[900,750],[600,850],[100,800],[-50,500],[100,300],[-50,150]]],
    ['thunder-valley', 'Thunder Valley', [[0,0],[300,-100],[600,0],[800,-150],[1100,0],[1150,300],[950,450],[1100,650],[900,850],[500,750],[300,900],[0,800],[100,550],[-100,350]]],
    ['snowbird-summit', 'Snowbird Summit', [[0,0],[500,0],[700,180],[500,330],[700,470],[950,400],[1120,600],[870,800],[450,740],[250,900],[-60,740],[60,470],[-100,250]]],
    ['breidinger-grand-prix', 'Breidinger Grand Prix', [[0,0],[800,0],[1100,100],[1200,350],[1000,450],[800,350],[600,450],[700,650],[1000,700],[1200,900],[900,1050],[400,1000],[200,850],[300,650],[100,450],[-100,250]]]
  ];
  function prepare(source) {
    const points = source.points;
    const segments = points.map((p, i) => {
      const q = points[(i + 1) % points.length];
      return {p, q, length: Math.hypot(q.x-p.x, q.y-p.y)};
    });
    let total = 0;
    segments.forEach(s => { s.start = total; total += s.length; });
    return {...source, segments, length: total, bounds: {
      minX: Math.min(...points.map(p => p.x))-100, maxX: Math.max(...points.map(p => p.x))+100,
      minY: Math.min(...points.map(p => p.y))-100, maxY: Math.max(...points.map(p => p.y))+100
    }};
  }
  const tracks = definitions.map(([id, name, points], i) => {
    const track = prepare({id, name, points: points.map(([x,y]) => ({x,y})), roadWidth: 72, laps: i===7 ? 4 : 3, features: []});
    for (const [fraction, kind] of [[.15,'zipper'],[.38,'puddle'],[.62,'zipper'],[.86,'oil']]) {
      const d = fraction * track.length, segment = track.segments.find(s => d < s.start+s.length);
      const t = (d-segment.start)/segment.length;
      track.features.push({kind, x: segment.p.x+(segment.q.x-segment.p.x)*t, y: segment.p.y+(segment.q.y-segment.p.y)*t, radius: 15});
    }
    return track;
  });
  function nearest(track, x, y) {
    let best = {distance: Infinity, progress: 0};
    for (const s of track.segments) {
      const t = clamp(((x-s.p.x)*(s.q.x-s.p.x)+(y-s.p.y)*(s.q.y-s.p.y))/(s.length*s.length),0,1);
      const distance = Math.hypot(x-s.p.x-(s.q.x-s.p.x)*t,y-s.p.y-(s.q.y-s.p.y)*t);
      if (distance < best.distance) best = {distance, progress:s.start+t*s.length};
    }
    return best;
  }
  class Game {
    constructor(canvas, onStatus, onFinish) {
      this.canvas=canvas; this.ctx=canvas.getContext('2d'); this.onStatus=onStatus; this.onFinish=onFinish;
      this.track=tracks[0]; this.cars=[]; this.keys=new Set(); this.touch=new Set(); this.quality='high';
      this.mode='preview'; this.elapsed=0; this.particles=[]; this.last=performance.now(); this.accumulator=0;
      this.width=800; this.height=450; this.snapshots=new Map(); this.playerId=null;
      this.canvas.addEventListener('keydown', e => {
        if (['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','w','a','s','d','W','A','S','D',' '].includes(e.key)) { e.preventDefault(); this.keys.add(e.key.toLowerCase()); }
        if (e.key.toLowerCase()==='r' && this.mode==='practice') this.start(this.track.id);
      });
      this.canvas.addEventListener('keyup', e => this.keys.delete(e.key.toLowerCase()));
      window.addEventListener('blur', () => this.release());
      this.canvas.addEventListener('blur', () => this.release());
      document.addEventListener('visibilitychange', () => {if(document.hidden) this.release();});
      document.querySelectorAll('[data-control]').forEach(button => {
        const key=button.dataset.control;
        button.addEventListener('pointerdown', e => { e.preventDefault(); button.setPointerCapture(e.pointerId); this.touch.add(key); button.classList.add('active'); });
        const up=() => {this.touch.delete(key);button.classList.remove('active');};
        button.addEventListener('pointerup',up);button.addEventListener('pointercancel',up);button.addEventListener('lostpointercapture',up);
      });
      this.observer=new ResizeObserver(() => this.resize());this.observer.observe(canvas);
      requestAnimationFrame(t => this.frame(t));
    }
    release(){this.keys.clear();this.touch.clear();document.querySelectorAll('[data-control]').forEach(b=>b.classList.remove('active'));}
    resize(){
      const rect=this.canvas.getBoundingClientRect(), cap=this.quality==='ultra'?2.5:this.quality==='high'?1.75:1;
      const ratio=Math.min(window.devicePixelRatio||1,cap);
      this.width=rect.width;this.height=rect.height;
      this.canvas.width=Math.round(rect.width*ratio);this.canvas.height=Math.round(rect.height*ratio);
      this.ctx.setTransform(ratio,0,0,ratio,0,0);
    }
    input(){
      const has=(...keys)=>keys.some(k=>this.keys.has(k));
      let accelerate=has('arrowup','w')||this.touch.has('gas'), brake=has('arrowdown','s')||this.touch.has('brake');
      let steer=(has('arrowright','d')||this.touch.has('right')?1:0)-(has('arrowleft','a')||this.touch.has('left')?1:0);
      try{
        const pad=Array.from(navigator.getGamepads ? navigator.getGamepads() : []).find(Boolean);
        if(pad){if(Math.abs(pad.axes[0]||0)>.12)steer=clamp(pad.axes[0],-1,1);accelerate=accelerate||!!pad.buttons[7]?.pressed||!!pad.buttons[0]?.pressed;brake=brake||!!pad.buttons[6]?.pressed||!!pad.buttons[1]?.pressed;}
      }catch{ /* Gamepads may be blocked by a browser's permissions policy. */ }
      return {accelerate,brake,steer,fire:false};
    }
    start(id){
      this.mode='practice';this.track=tracks.find(t=>t.id===id)||tracks[0];this.elapsed=0;this.countdown=3;this.finishSent=false;this.particles=[];this.snapshots.clear();this.accumulator=0;this.release();
      const p=this.track.points[0], q=this.track.points[1], heading=Math.atan2(q.y-p.y,q.x-p.x);
      this.cars=Array.from({length:4},(_,i)=>({
        id:i===0?'you':`ai-${i}`, name:i===0?'You':['','Apex AI','Slipstream AI','Redline AI'][i],
        x:p.x-Math.cos(heading)*(Math.floor(i/2)*34+12)+Math.sin(heading)*(i%2?15:-15),
        y:p.y-Math.sin(heading)*(Math.floor(i/2)*34+12)-Math.cos(heading)*(i%2?15:-15),
        heading,speed:0,lap:0,next:1,checkpoints:0,finished:false,place:0,boost:0,slip:0,cooldown:0,color:colors[i]
      }));
      this.canvas.focus({preventScroll:true});
    }
    preview(id){if(this.mode==='online')return;this.mode='preview';this.track=tracks.find(t=>t.id===id)||tracks[0];this.cars=[];}
    online(playerId, serverTracks){
      this.mode='online';this.playerId=playerId;this.serverTracks=serverTracks.map(prepare);this.snapshots.clear();this.cars=[];this.release();
    }
    snapshot(data){
      if(this.mode!=='online')return;
      const now=performance.now(), prior=this.snapshots.get(data.matchId);
      this.snapshots.set(data.matchId,{previous:prior?.current||data,current:data,time:now,interval:prior?clamp(now-prior.time,16,200):50});
      if(this.snapshots.size>8)this.snapshots.delete(this.snapshots.keys().next().value);
      const own=[...this.snapshots.values()].find(s=>s.current.cars.some(c=>c.id===this.playerId)&&s.current.state!=='finished');
      if(own)this.matchId=own.current.matchId;
      else if(!this.matchId || data.matchId===this.matchId || !this.snapshots.has(this.matchId) || this.snapshots.get(this.matchId).current.state==='finished')this.matchId=data.matchId;
    }
    leaveOnline(){if(this.mode==='online'){this.mode='preview';this.cars=[];this.track=tracks[0];this.snapshots.clear();this.matchId=null;this.release();}}
    drivingOnline(){
      const s=this.snapshots.get(this.matchId)?.current;
      return this.mode==='online'&&s?.state==='racing'&&s.cars.some(c=>c.id===this.playerId&&!c.finished);
    }
    step(dt){
      if(this.mode!=='practice')return;
      if(this.countdown>0){this.countdown=Math.max(0,this.countdown-dt);return;}
      this.elapsed+=dt;
      for(let i=0;i<this.cars.length;i++){
        const c=this.cars[i];if(c.finished)continue;
        let input=this.input();
        if(i>0){
          const p=this.track.points[c.next], bearing=Math.atan2(p.y-c.y,p.x-c.x), error=angle(bearing-c.heading);
          input={accelerate:Math.abs(error)<.9&&c.speed<(i===1?185:175),brake:Math.abs(error)>.75&&c.speed>70,steer:clamp(error*2.4,-1,1)};
        }
        const road=nearest(this.track,c.x,c.y), offroad=road.distance>this.track.roadWidth/2;
        c.boost=Math.max(0,c.boost-dt);c.slip=Math.max(0,c.slip-dt);c.cooldown=Math.max(0,c.cooldown-dt);
        c.speed+=(input.accelerate?135:0)*dt-(input.brake?230:0)*dt;
        c.speed=Math.max(0,c.speed-(offroad?c.speed*1.9+18:c.speed*.3+6)*dt);
        c.speed=Math.min(c.speed,c.boost?320:230);
        if(c.boost)c.speed=Math.min(320,c.speed+190*dt);
        const grip=c.slip?.55:1;
        c.heading+=input.steer*2.8*clamp(c.speed/100,0,1)*grip*dt;
        c.x+=Math.cos(c.heading)*c.speed*dt;c.y+=Math.sin(c.heading)*c.speed*dt;
        const b=this.track.bounds;c.x=clamp(c.x,b.minX,b.maxX);c.y=clamp(c.y,b.minY,b.maxY);
        if(!c.cooldown)for(const f of this.track.features){
          if(Math.hypot(c.x-f.x,c.y-f.y)<f.radius+8){
            if(f.kind==='zipper')c.boost=1.1;else{c.slip=1.1;c.speed*=f.kind==='puddle'?.72:.85;}
            c.cooldown=1.2;break;
          }
        }
        const target=this.track.points[c.next];
        if(Math.hypot(c.x-target.x,c.y-target.y)<this.track.roadWidth*.65){
          c.checkpoints++;if(c.next===0){
            c.lap++;
            if(c.lap>=this.track.laps){c.finished=true;c.finishTime=this.elapsed;c.place=this.cars.filter(v=>v.finished).length;c.speed=0;}
          }
          c.next=(c.next+1)%this.track.points.length;
        }
        if(this.quality!=='low'&&c.speed>120&&Math.random()<dt*20&&this.particles.length<(this.quality==='ultra'?120:55)){
          this.particles.push({x:c.x-Math.cos(c.heading)*16,y:c.y-Math.sin(c.heading)*16,life:.5,color:c.boost?'#e5ba6b':'#aaa'});
        }
      }
      // Soft separation preserves steering while making contact readable.
      for(let i=0;i<this.cars.length;i++)for(let j=i+1;j<this.cars.length;j++){
        const a=this.cars[i],b=this.cars[j],dx=a.x-b.x,dy=a.y-b.y,d=Math.hypot(dx,dy);
        if(d>0&&d<21&&!a.finished&&!b.finished){const shift=(21-d)/2;a.x+=dx/d*shift;a.y+=dy/d*shift;b.x-=dx/d*shift;b.y-=dy/d*shift;a.speed*=.98;b.speed*=.98;}
      }
      this.particles=this.particles.filter(p=>(p.life-=dt)>0);
      const you=this.cars[0];
      if(you.finished&&!this.finishSent){this.finishSent=true;this.onFinish(`Finished ${ordinal(you.place)} · ${formatTime(you.finishTime)}. Race again to improve your line.`);}
    }
    frame(time){
      const delta=Math.min((time-this.last)/1000,.1);this.last=time;
      if(!document.hidden){this.accumulator+=delta;let steps=0;while(this.accumulator>=1/60&&steps++<6){this.step(1/60);this.accumulator-=1/60;}this.render(time);}
      else this.accumulator=0;
      requestAnimationFrame(t=>this.frame(t));
    }
    render(time){
      let state=null;
      if(this.mode==='online'){
        const pair=this.snapshots.get(this.matchId);
        if(pair){
          state=pair.current;this.track=this.serverTracks.find(t=>t.id===state.trackId)||this.track;
          const t=clamp((time-pair.time)/pair.interval,0,1);
          this.cars=state.cars.map((c,i)=>{
            const prev=pair.previous.cars.find(p=>p.id===c.id);
            return {...c,color:colors[i%colors.length],x:prev?prev.x+(c.x-prev.x)*t:c.x,y:prev?prev.y+(c.y-prev.y)*t:c.y,heading:prev?prev.heading+angle(c.heading-prev.heading)*t:c.heading};
          });
        }else this.cars=[];
      }
      const ctx=this.ctx,w=this.width,h=this.height,b=this.track.bounds;
      ctx.clearRect(0,0,w,h);ctx.fillStyle='#14211e';ctx.fillRect(0,0,w,h);
      const scale=Math.min(w/(b.maxX-b.minX),h/(b.maxY-b.minY));
      const ox=(w-(b.maxX-b.minX)*scale)/2-b.minX*scale,oy=(h-(b.maxY-b.minY)*scale)/2-b.minY*scale;
      ctx.save();ctx.translate(ox,oy);ctx.scale(scale,scale);
      ctx.strokeStyle='#263b31';ctx.lineWidth=1/scale;
      if(this.quality!=='low'){for(let x=b.minX;x<b.maxX;x+=90){ctx.beginPath();ctx.moveTo(x,b.minY);ctx.lineTo(x,b.maxY);ctx.stroke();}for(let y=b.minY;y<b.maxY;y+=90){ctx.beginPath();ctx.moveTo(b.minX,y);ctx.lineTo(b.maxX,y);ctx.stroke();}}
      const path=()=>{ctx.beginPath();this.track.points.forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));ctx.closePath();};
      ctx.lineJoin='round';path();ctx.strokeStyle='#9f4f49';ctx.lineWidth=this.track.roadWidth+10;ctx.stroke();path();ctx.strokeStyle='#34383b';ctx.lineWidth=this.track.roadWidth;ctx.stroke();
      path();ctx.setLineDash([12,16]);ctx.strokeStyle='#b5b29c70';ctx.lineWidth=2;ctx.stroke();ctx.setLineDash([]);
      for(const f of this.track.features){
        ctx.save();ctx.translate(f.x,f.y);
        if(f.kind==='zipper'){ctx.strokeStyle='#e5ba6b';ctx.lineWidth=4;const segment=nearest(this.track,f.x,f.y);const s=this.track.segments.find(s=>segment.progress<=s.start+s.length)||this.track.segments[0];ctx.rotate(Math.atan2(s.q.y-s.p.y,s.q.x-s.p.x));for(let i=-10;i<=10;i+=10){ctx.beginPath();ctx.moveTo(i-5,-10);ctx.lineTo(i+3,0);ctx.lineTo(i-5,10);ctx.stroke();}}
        else{ctx.fillStyle=f.kind==='oil'?'#101215':'#5486a399';ctx.beginPath();ctx.ellipse(0,0,f.radius,f.radius*.7,.3,0,TAU);ctx.fill();}
        ctx.restore();
      }
      const start=this.track.points[0], next=this.track.points[1];
      ctx.save();ctx.translate(start.x,start.y);ctx.rotate(Math.atan2(next.y-start.y,next.x-start.x));
      for(let row=0;row<2;row++)for(let col=0;col<8;col++){ctx.fillStyle=(row+col)%2?'#151719':'#eee8dc';ctx.fillRect(row*7,-this.track.roadWidth/2+col*this.track.roadWidth/8,7,this.track.roadWidth/8);}ctx.restore();
      for(const p of this.particles){ctx.globalAlpha=p.life;ctx.fillStyle=p.color;ctx.beginPath();ctx.arc(p.x,p.y,4*(1-p.life),0,TAU);ctx.fill();}ctx.globalAlpha=1;
      for(const c of this.cars){
        ctx.save();ctx.translate(c.x,c.y);ctx.rotate(c.heading);
        if(this.quality!=='low'){ctx.shadowColor='#0009';ctx.shadowBlur=8;ctx.shadowOffsetY=4;}
        ctx.fillStyle=c.color;ctx.fillRect(-15,-8,30,16);ctx.shadowColor='transparent';
        ctx.fillStyle='#0c1117';ctx.fillRect(0,-6,7,12);ctx.fillRect(-9,-6,4,12);ctx.fillStyle='#f3db9d';ctx.fillRect(12,-6,2,4);ctx.fillRect(12,2,2,4);
        if(c.id==='you'||c.id===this.playerId){ctx.strokeStyle='#fff';ctx.lineWidth=2;ctx.strokeRect(-17,-10,34,20);}
        ctx.restore();ctx.font=`600 ${Math.max(12,10/scale)}px system-ui`;ctx.textAlign='center';ctx.fillStyle='#fff';ctx.fillText(c.name,c.x,c.y-22);
      }
      ctx.restore();
      if(time-(this.statusTime||0)>100){
        this.statusTime=time;
        if(this.mode==='practice'){
          const you=this.cars[0],rank=[...this.cars].sort((a,b)=>(b.finished?1:0)-(a.finished?1:0)||b.checkpoints-a.checkpoints||Math.hypot(a.x-this.track.points[a.next].x,a.y-this.track.points[a.next].y)-Math.hypot(b.x-this.track.points[b.next].x,b.y-this.track.points[b.next].y)).indexOf(you)+1;
          this.onStatus(this.countdown>0?`Starting in ${Math.ceil(this.countdown)}…`:you.finished?`Checkered flag · ${ordinal(you.place)}`:`${Math.round(you.speed)} km/h${you.boost?' · BOOST':''}`,`Lap ${Math.min(you.lap+1,this.track.laps)}/${this.track.laps} · ${ordinal(you.finished?you.place:rank)} / 4 · ${formatTime(this.elapsed)}`);
        }else if(state){
          const mine=this.cars.find(c=>c.id===this.playerId);
          const winner=this.cars.find(c=>c.id===state.winnerId);
          this.onStatus(state.state==='finished'?`Server result: ${winner?`${winner.name} wins`:'No winner · DNF'}`:state.state==='countdown'?`Server countdown · ${Math.ceil(state.countdown)}`:mine?`Online · ${Math.round(mine.speed)} km/h`:'Spectating · live server race',`Round ${state.round||1} · ${mine?`Lap ${Math.min(mine.lap+1,this.track.laps)}/${this.track.laps} · `:''}${formatTime(state.elapsed)}`);
        }else this.onStatus(this.mode==='online'?'Waiting for server race…':'Choose a circuit and start your engines.',`${this.track.name} · ${this.track.laps} laps`);
      }
    }
  }
  function ordinal(n){return `${n}${n===1?'st':n===2?'nd':n===3?'rd':'th'}`;}
  function formatTime(n){return `${Math.floor((n||0)/60)}:${((n||0)%60).toFixed(1).padStart(4,'0')}`;}
  return {Game,tracks};
})();
