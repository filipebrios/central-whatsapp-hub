-- Vínculo explícito e idempotente. Não importa eventos nem armazena tokens Google.
create unique index agenda_calendar_links_one_event_per_appointment
on public.agenda_calendar_links(company_id, appointment_id, calendar_id);

alter table public.agenda_calendar_links enable row level security;
revoke all on table public.agenda_calendar_links from anon, authenticated;
grant select, insert on table public.agenda_calendar_links to authenticated;

create policy agenda_calendar_links_insert
on public.agenda_calendar_links for insert to authenticated
with check (app_private.agenda_has_access(company_id));

create or replace function public.agenda_link_google_event(
  p_company_id uuid, p_appointment_id uuid, p_calendar_id text,
  p_event_id text, p_event_etag text, p_expected_revision integer
) returns public.agenda_calendar_links
language plpgsql security invoker set search_path = ''
as $$
declare
  current_revision integer;
  existing public.agenda_calendar_links;
begin
  if (select auth.uid()) is null then
    raise exception using errcode='42501', message='Autenticação necessária';
  end if;
  if p_calendar_id is null or length(btrim(p_calendar_id))=0 or length(p_calendar_id)>1024
     or p_event_id is null or length(btrim(p_event_id))=0 or length(p_event_id)>1024
     or p_event_etag is null or length(btrim(p_event_etag))=0 or length(p_event_etag)>256
     or p_expected_revision is null or p_expected_revision<1 then
    raise exception using errcode='22023', message='Identidade do vínculo incompleta';
  end if;
  -- A linha é visível/alterável somente para um membro ativo via RLS.
  -- O lock serializa duas tentativas de vincular o mesmo atendimento.
  select a.revision into current_revision from public.agenda_appointments a
  where a.company_id=p_company_id and a.id=p_appointment_id and a.deleted_at is null
  for update;
  if not found then
    raise exception using errcode='42501', message='Atendimento indisponível para esta conta';
  end if;
  select * into existing from public.agenda_calendar_links l
  where l.company_id=p_company_id and l.calendar_id=p_calendar_id and l.event_id=p_event_id;
  if found then
    if existing.appointment_id<>p_appointment_id then
      raise exception using errcode='23505', message='Evento já vinculado a outro atendimento';
    end if;
    return existing; -- repetição da mesma operação não altera nem duplica nada
  end if;
  if current_revision<>p_expected_revision then
    raise exception using errcode='40001', message='Atendimento alterado; atualize antes de vincular';
  end if;
  if exists(select 1 from public.agenda_calendar_links l
    where l.company_id=p_company_id and l.appointment_id=p_appointment_id
      and l.calendar_id=p_calendar_id) then
    raise exception using errcode='23505', message='Atendimento já vinculado a outro evento';
  end if;
  insert into public.agenda_calendar_links(
    company_id,appointment_id,calendar_id,event_id,event_etag,sync_status
  ) values(p_company_id,p_appointment_id,p_calendar_id,p_event_id,p_event_etag,'vinculado')
  returning * into existing;
  return existing;
end;
$$;
revoke all on function public.agenda_link_google_event(uuid,uuid,text,text,text,integer) from public, anon;
grant execute on function public.agenda_link_google_event(uuid,uuid,text,text,text,integer) to authenticated;
