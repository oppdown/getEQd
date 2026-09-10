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
const profileFile = document.querySelector('#profileFile');
const profileList = document.querySelector('#profileList');
const profileStatus = document.querySelector('#profileStatus');
const profileStorageKey = 'geteqd-imported-profiles-v1';
const targetFrequencies = [32,64,250,2500,6400,12000];

function safeProfiles(){
  try { return JSON.parse(localStorage.getItem(profileStorageKey) || '[]'); }
  catch { return []; }
}
function formatDate(value){
  if(!value) return 'Date not supplied';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined,{year:'numeric',month:'short',day:'numeric'});
}
function validateProfile(candidate){
  if(!candidate || candidate.schema !== 'getEQd-profile/v1') throw new Error('Schema must be getEQd-profile/v1.');
  if(typeof candidate.model !== 'string' || !candidate.model.trim()) throw new Error('Add a model name.');
  if(!Array.isArray(candidate.frequenciesHz) || !Array.isArray(candidate.gainDb) || candidate.frequenciesHz.length !== candidate.gainDb.length || candidate.frequenciesHz.length < 2) throw new Error('Frequencies and gainDb must be matching arrays with at least two points.');
  if(candidate.frequenciesHz.some((value,i)=>!Number.isFinite(value) || value <= 0 || !Number.isFinite(candidate.gainDb[i]))) throw new Error('Frequency and gain values must be finite numbers.');
  return {schema:'getEQd-profile/v1',model:candidate.model.trim(),source:typeof candidate.source === 'string' && candidate.source.trim() ? candidate.source.trim() : 'Local import',measuredAt:candidate.measuredAt || '',notes:typeof candidate.notes === 'string' ? candidate.notes.trim() : '',frequenciesHz:candidate.frequenciesHz.map(Number),gainDb:candidate.gainDb.map(Number)};
}
function interpolate(profile, frequency){
  const points = profile.frequenciesHz.map((hz,i)=>({hz,gain:profile.gainDb[i]})).sort((a,b)=>a.hz-b.hz);
  if(frequency <= points[0].hz) return points[0].gain;
  if(frequency >= points[points.length-1].hz) return points[points.length-1].gain;
  const upper = points.findIndex(point=>point.hz >= frequency); const low = points[upper-1]; const high = points[upper];
  const ratio = (Math.log(frequency)-Math.log(low.hz))/(Math.log(high.hz)-Math.log(low.hz));
  return low.gain + (high.gain-low.gain)*ratio;
}
function applyProfile(profile){
  targetFrequencies.forEach((frequency,index)=>{bands[index].value=Math.max(-12,Math.min(11,Math.round(interpolate(profile,frequency)*2)/2));});
  document.querySelectorAll('.preset').forEach(button=>button.classList.remove('active')); renderBands(); sync(); document.querySelector('#console').scrollIntoView({behavior:'smooth',block:'start'});
  profileStatus.textContent = `${profile.model} applied to the six-band preview. Original measurement remains unchanged.`;
}
function renderProfiles(){
  const profiles=safeProfiles();
  profileList.innerHTML = profiles.length ? profiles.map((profile,index)=>`<article class="profile-card"><div class="profile-card-top"><span class="version-chip">LOCAL · MEASURED</span><span class="profile-count">${profile.frequenciesHz.length} points</span></div><h3>${profile.model}</h3><p>${profile.source} · ${formatDate(profile.measuredAt)}</p>${profile.notes?`<p class="profile-notes">${profile.notes}</p>`:''}<button class="profile-apply" type="button" data-profile-index="${index}">Audition in console <span>→</span></button></article>`).join('') : '<div class="profile-empty"><strong>Your measured profiles will live here.</strong><p>Import a JSON measurement to create the first local profile. No account or upload is involved.</p></div>';
  profileList.querySelectorAll('[data-profile-index]').forEach(button=>button.addEventListener('click',()=>applyProfile(profiles[+button.dataset.profileIndex])));
}

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
document.querySelector('#importProfile').addEventListener('click',()=>profileFile.click());
profileFile.addEventListener('change',async event=>{
  const file=event.target.files[0]; if(!file) return;
  try { const profile=validateProfile(JSON.parse(await file.text())); const profiles=safeProfiles().filter(item=>item.model.toLowerCase()!==profile.model.toLowerCase()); profiles.unshift(profile); localStorage.setItem(profileStorageKey,JSON.stringify(profiles)); renderProfiles(); profileStatus.textContent=`Imported ${profile.model}. The source file stays on your device.`; }
  catch(error) { profileStatus.textContent=`Import not accepted: ${error.message}`; }
  event.target.value='';
});
document.querySelector('#downloadTemplate').addEventListener('click',()=>{
  const template={schema:'getEQd-profile/v1',model:'Your headphone or speaker model',source:'Measurement source or rig',measuredAt:'2026-09-10',notes:'Add how this measurement was made.',frequenciesHz:[20,32,64,125,250,500,1000,2000,4000,8000,12000,16000,20000],gainDb:[0,0,0,0,0,0,0,0,0,0,0,0,0]};
  const link=document.createElement('a'); link.href=URL.createObjectURL(new Blob([JSON.stringify(template,null,2)],{type:'application/json'})); link.download='getEQd-profile-template.json'; link.click(); URL.revokeObjectURL(link.href);
});
renderBands(); sync(); syncHeadphones();
renderProfiles();
