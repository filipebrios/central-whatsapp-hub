-- Capabilities: raw token is returned once; only its SHA-256 digest is stored.
create table public.agenda_response_tokens (
  token_hash text primary key check(token_hash ~ '^[0-9a-f]{64}$'),
  company_id uuid not null,
  appointment_id uuid not null,
  kind text not null check(kind in ('confirmacao','pos_venda')),
  schedule_date date,
  schedule_time time,
  completed_date date,
  created_by uuid references auth.users(id),
  created_at timestamptz not null default now(),
  expires_at timestamptz not null,
  used_at timestamptz,
  response_action text check(response_action in ('confirm','cancel','feedback')),
  foreign key(company_id,appointment_id) references public.agenda_appointments(company_id,id)
);
create index agenda_response_appointment on public.agenda_response_tokens(company_id,appointment_id,kind);
alter table public.agenda_response_tokens enable row level security;
revoke all on public.agenda_response_tokens from anon,authenticated;
grant select on public.agenda_response_tokens to authenticated;
grant insert(token_hash,company_id,appointment_id,kind) on public.agenda_response_tokens to authenticated;
grant all on public.agenda_response_tokens to service_role;
create policy agenda_response_read on public.agenda_response_tokens for select to authenticated
using(app_private.agenda_has_access(company_id));
create policy agenda_response_issue on public.agenda_response_tokens for insert to authenticated
with check(app_private.agenda_has_access(company_id));

create function app_private.agenda_response_stamp()
returns trigger language plpgsql security invoker set search_path=''
as $$
declare a public.agenda_appointments; today date := (now() at time zone 'America/Sao_Paulo')::date;
begin
  if (select auth.uid()) is null then raise insufficient_privilege; end if;
  select * into a from public.agenda_appointments
    where company_id=new.company_id and id=new.appointment_id and deleted_at is null for share;
  if not found then raise insufficient_privilege; end if;
  if new.kind='confirmacao' then
    if a.status not in ('agendado','confirmado','revisao') or a.date is null or a.time is null or a.date<today then
      raise exception using errcode='22023',message='Atendimento não está disponível para confirmação';
    end if;
    new.schedule_date=a.date; new.schedule_time=a.time; new.completed_date=null;
    new.expires_at=least(now()+interval '14 days',((a.date+1)::timestamp at time zone 'America/Sao_Paulo'));
  elsif new.kind='pos_venda' then
    if a.status<>'concluido' or a.service_completed_date is null or a.service_completed_date>today-7 or a.feedback_score is not null then
      raise exception using errcode='22023',message='Pós-venda não está disponível';
    end if;
    new.schedule_date=null; new.schedule_time=null; new.completed_date=a.service_completed_date;
    new.expires_at=now()+interval '14 days';
  else raise invalid_parameter_value;
  end if;
  new.created_by=(select auth.uid()); new.created_at=now(); new.used_at=null; new.response_action=null;
  return new;
end $$;
create trigger agenda_response_stamp before insert on public.agenda_response_tokens
for each row execute function app_private.agenda_response_stamp();

create function public.agenda_issue_response_token(p_company_id uuid,p_appointment_id uuid,p_kind text,p_expected_revision integer)
returns jsonb language plpgsql security invoker set search_path=''
as $$
declare a public.agenda_appointments; raw_token text; expiry timestamptz;
begin
  if (select auth.uid()) is null then raise insufficient_privilege; end if;
  select * into a from public.agenda_appointments where company_id=p_company_id and id=p_appointment_id and deleted_at is null for share;
  if not found then raise insufficient_privilege; end if;
  if a.revision is distinct from p_expected_revision then
    raise exception using errcode='40001',message='Atendimento mudou. Atualize antes de preparar o link';
  end if;
  raw_token=encode(extensions.gen_random_bytes(32),'hex');
  insert into public.agenda_response_tokens(token_hash,company_id,appointment_id,kind)
  values(encode(extensions.digest(raw_token,'sha256'),'hex'),p_company_id,p_appointment_id,p_kind)
  returning expires_at into expiry;
  return jsonb_build_object('token',raw_token,'expires_at',expiry);
end $$;
revoke all on function public.agenda_issue_response_token(uuid,uuid,text,integer) from public,anon;
grant execute on function public.agenda_issue_response_token(uuid,uuid,text,integer) to authenticated;

-- Only the server handler can call this function. A GET/preview never calls a write operation.
create function public.agenda_respond(p_token text,p_operation text,p_score integer default null,p_note text default '')
returns jsonb language plpgsql security invoker set search_path=''
as $$
declare t public.agenda_response_tokens; a public.agenda_appointments; h text;
  today date := (now() at time zone 'America/Sao_Paulo')::date;
begin
  if p_token is null or p_token !~ '^[0-9a-f]{64}$' or p_operation is null or p_operation not in ('lookup','confirm','cancel','feedback') then
    return jsonb_build_object('status','unavailable');
  end if;
  h=encode(extensions.digest(p_token,'sha256'),'hex');
  select * into t from public.agenda_response_tokens where token_hash=h;
  if not found then return jsonb_build_object('status','unavailable'); end if;
  -- Appointment first, token second: same ordering as issue/reconcile/manual queue.
  select * into a from public.agenda_appointments where company_id=t.company_id and id=t.appointment_id for update;
  select * into t from public.agenda_response_tokens where token_hash=h for update;
  if a.id is null or a.deleted_at is not null or t.expires_at<=now() then
    return jsonb_build_object('status','unavailable');
  end if;
  if t.used_at is not null then return jsonb_build_object('status','recorded'); end if;
  if t.kind='confirmacao' then
    if a.status not in ('agendado','confirmado','revisao') or a.date is distinct from t.schedule_date
      or a.time is distinct from t.schedule_time or a.date<today then
      return jsonb_build_object('status','unavailable');
    end if;
    if p_operation='lookup' then
      return jsonb_build_object('status','available','kind',t.kind,'service',a.primary_service,'date',a.date,'time',to_char(a.time,'HH24:MI'));
    end if;
    if p_operation not in ('confirm','cancel') then return jsonb_build_object('status','unavailable'); end if;
    update public.agenda_appointments set status=case when p_operation='confirm' then 'confirmado' else 'reagendar' end
      where company_id=a.company_id and id=a.id;
  else
    if a.status<>'concluido' or a.service_completed_date is distinct from t.completed_date
      or a.service_completed_date>today-7 or a.feedback_score is not null then
      return jsonb_build_object('status','unavailable');
    end if;
    if p_operation='lookup' then return jsonb_build_object('status','available','kind',t.kind); end if;
    if p_operation<>'feedback' or p_score is null or p_score<0 or p_score>10 or length(coalesce(p_note,''))>4000 then
      return jsonb_build_object('status','unavailable');
    end if;
    update public.agenda_appointments set feedback_score=p_score,feedback_note=coalesce(p_note,'')
      where company_id=a.company_id and id=a.id;
  end if;
  update public.agenda_response_tokens set used_at=now(),response_action=p_operation where token_hash=h;
  return jsonb_build_object('status','recorded');
end $$;
revoke all on function public.agenda_respond(text,text,integer,text) from public,anon,authenticated;
grant execute on function public.agenda_respond(text,text,integer,text) to service_role;

-- A cancellation request must stop unsent reminders; the Google event still needs operator review.
create or replace function app_private.agenda_close_stale_messages()
returns trigger language plpgsql security invoker set search_path=''
as $$
begin
  update public.agenda_message_queue q set state='cancelada'
  where q.company_id=new.company_id and q.appointment_id=new.id and q.state in ('preparada','erro') and (
    new.status in ('cancelado','cancelar') or
    (new.status='reagendar' and q.template_key in ('confirmacao','lembrete_d1','lembrete_d0')) or
    (q.template_key in ('confirmacao','lembrete_d1','lembrete_d0','reagendamento') and
      (new.status='concluido' or q.occurrence_key is distinct from new.date::text||'T'||to_char(new.time,'HH24:MI'))) or
    (q.template_key in ('pos_venda','avaliacao_google') and
      (new.status<>'concluido' or q.occurrence_key is distinct from new.service_completed_date::text)) or
    (q.template_key='avaliacao_google' and coalesce(new.feedback_score,-1)<8)
  );
  if new.feedback_score is not null then
    update public.agenda_message_queue q set state='tratada_manual'
    where q.company_id=new.company_id and q.appointment_id=new.id and q.template_key='pos_venda' and q.state in ('preparada','erro');
  end if;
  return null;
end $$;
