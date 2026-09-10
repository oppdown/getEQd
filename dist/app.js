const bands = [
  {name:'Sub foundation', role:'Room / weight', hz:'32 Hz', value:0},
  {name:'Kick body', role:'Punch / impact', hz:'64 Hz', value:0},
  {name:'Low-mid', role:'Warmth / mud', hz:'250 Hz', value:0},
  {name:'Presence', role:'Voice / attack', hz:'2.5 kHz', value:0},
  {name:'Detail', role:'Clarity / bite', hz:'6.4 kHz', value:0},
  {name:'Air', role:'Space / shimmer', hz:'12 kHz', value:0}
];
const presets = {
  reference:[0,0,0,0,0,0], impact:[4,3,-1.5,1,1.5,.5], dialogue:[-2,-1,-2,2.5,1.5,0], night:[-3,-2,1,1,-1,-2]
};
const grid = document.querySelector('#bandGrid');
const curveLine = document.querySelector('#curveLine');
const curveFill = document.querySelector('#curveFill');
const curveReadout = document.querySelector('#curveReadout');
const preamp = document.querySelector('#preamp');
const limiter = document.querySelector('#limiter');
const outputMode = document.querySelector('#outputMode');
const headphoneDeck = document.querySelector('#headphoneDeck');
const headphoneTarget = document.querySelector('#headphoneTarget');
const headphoneSoftware = document.querySelector('#headphoneSoftware');
const crossfeed = document.querySelector('#crossfeed');
const stageWidth = document.querySelector('#stageWidth');

function renderBands(){
  grid.innerHTML = bands.map((b,i)=>`<article class="band-card"><div class="band-top"><span class="band-index">0${i+1}</span><span class="band-index">${b.value > 0 ? '+' : ''}${b.value.toFixed(1)}</span></div><div class="band-name">${b.name}</div><div class="band-role">${b.role}</div><div class="band-value" id="bandValue${i}">${b.value > 0 ? '+' : ''}${b.value.toFixed(1)} dB</div><div class="band-hz">${b.hz}</div><input data-band="${i}" type="range" min="-12" max="11" step="0.5" value="${b.value}" aria-label="${b.name} gain"></article>`).join('');
  grid.querySelectorAll('input').forEach(input=>input.addEventListener('input',e=>{bands[+e.target.dataset.band].value=+e.target.value; sync();}));
}
function curvePoints(){
  const gains=bands.map(b=>b.value); const points=[]; const width=1000; const height=300;
  for(let x=0;x<=width;x+=10){const t=x/width; const pos=t*(gains.length-1); const i=Math.min(gains.length-2,Math.floor(pos)); const f=pos-i; const smooth=f*f*(3-2*f); const gain=gains[i]+(gains[i+1]-gains[i])*smooth+(+preamp.value); const y=height/2-(gain/12)*(height*.43); points.push(`${x.toFixed(1)},${Math.max(8,Math.min(height-8,y)).toFixed(1)}`)} return points;
}
function sync(){
  bands.forEach((b,i)=>{const el=document.querySelector(`#bandValue${i}`); if(el)el.textContent=`${b.value>0?'+':''}${b.value.toFixed(1)} dB`; const top=document.querySelectorAll('.band-top .band-index')[i*2+1]; if(top)top.textContent=`${b.value>0?'+':''}${b.value.toFixed(1)}`;});
  const pts=curvePoints(); curveLine.setAttribute('d',`M ${pts.join(' L ')}`); curveFill.setAttribute('d',`M 0 150 L ${pts.join(' L ')} L 1000 150 Z`);
  const avg=bands.reduce((a,b)=>a+b.value,0)/bands.length + +preamp.value; curveReadout.textContent=`${avg>0?'+':''}${avg.toFixed(1)} dB`; document.querySelector('#preampValue').textContent=`${+preamp.value>0?'+':''}${(+preamp.value).toFixed(1)} dB`; document.querySelector('#limiterValue').textContent=`${(+limiter.value).toFixed(1)} dB`;
}
function syncHeadphones(){
  const active = outputMode.value.startsWith('Headphones');
  headphoneDeck.hidden = !active;
  document.querySelector('#crossfeedValue').textContent = `${crossfeed.value}%`;
  document.querySelector('#stageWidthValue').textContent = `${stageWidth.value}%`;
  headphoneDeck.style.setProperty('--stage-width', `${stageWidth.value}%`);
  headphoneDeck.dataset.target = headphoneTarget.value;
  headphoneDeck.dataset.software = headphoneSoftware.value;
}
document.querySelectorAll('.preset').forEach(button=>button.addEventListener('click',()=>{document.querySelectorAll('.preset').forEach(b=>b.classList.remove('active'));button.classList.add('active');presets[button.dataset.preset].forEach((v,i)=>{bands[i].value=v;});renderBands();sync();}));
preamp.addEventListener('input',sync); limiter.addEventListener('input',sync);
outputMode.addEventListener('change',syncHeadphones); headphoneTarget.addEventListener('change',syncHeadphones); headphoneSoftware.addEventListener('change',syncHeadphones); crossfeed.addEventListener('input',syncHeadphones); stageWidth.addEventListener('input',syncHeadphones);
document.querySelector('#bypass').addEventListener('click',e=>{const on=e.currentTarget.classList.toggle('on');e.currentTarget.setAttribute('aria-pressed',on);e.currentTarget.innerHTML=`<span class="toggle-dot"></span> ${on?'Processing bypassed':'Bypass'}`; document.querySelector('#console').classList.toggle('bypassed',on);});
renderBands(); sync(); syncHeadphones();
